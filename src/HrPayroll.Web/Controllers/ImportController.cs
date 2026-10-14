using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>
/// استيراد ملفات البصمة: منطقة مستقلة لملف الدخول ومنطقة مستقلة لملف الخروج،
/// ثم معاينة موحّدة لكل ملف على حدة (مقبول/مكرر/يحتاج مراجعة/فترة البيانات)
/// وتأكيد الاستيراد مع معالجة الحضور.
/// </summary>
[Authorize(Policy = "ManageAttendance")]
public class ImportController : Controller
{
    private const long MaxFileSize = 25 * 1024 * 1024; // 25 MB لكل ملف
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xls", ".csv", ".xml", ".txt" };
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "hrpayroll-import");

    /// <summary>أقصى فترة تُعالج تلقائيًا بعد الاستيراد (حماية من نطاق تاريخ غير منطقي في الملف).</summary>
    private const int MaxAutoProcessDays = 92;

    private readonly AttendanceImportService _import;
    private readonly AttendanceProcessingService _processing;
    private readonly AppDbContext _db;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        AttendanceImportService import,
        AttendanceProcessingService processing,
        AppDbContext db,
        ILogger<ImportController> logger)
    {
        _import = import;
        _processing = processing;
        _db = db;
        _logger = logger;
    }

    private const string EmployeeImportServiceMarker = HrPayroll.Infrastructure.Import.EmployeeImportService.EmployeeImportMarker;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Batches = await _db.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.ImportedAt).Take(30).ToListAsync(ct);
        return View();
    }

    /// <summary>
    /// يستقبل ملف الدخول و/أو ملف الخروج في منطقتين منفصلتين، ويتحقق من كل ملف على حدة
    /// دون كتابة أي بيانات، ثم يعرض معاينة مفصلة لكل ملف.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileSize * 2)]
    public async Task<IActionResult> Upload(IFormFile? entryFile, IFormFile? exitFile, CancellationToken ct)
    {
        var zones = new List<ImportFileZoneViewModel>
        {
            BuildZone("entry", "رفع ملف الدخول", "⬇️",
                "ملف تسجيل الدخول إلى العمل كما صُدِّر من جهاز البصمة.", "entryFile", entryFile),
            BuildZone("exit", "رفع ملف الخروج", "⬆️",
                "ملف تسجيل الخروج من العمل كما صُدِّر من جهاز البصمة.", "exitFile", exitFile)
        };

        if (zones.All(z => !z.Provided))
        {
            TempData["Error"] = "لم يتم اختيار أي ملف. اختر ملف الدخول و/أو ملف الخروج ثم أعد المحاولة.";
            return RedirectToAction(nameof(Index));
        }

        Directory.CreateDirectory(TempDir);

        foreach (var zone in zones.Where(z => z.Provided))
        {
            var file = zone.Key == "entry" ? entryFile! : exitFile!;

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                zone.Error = $"صيغة الملف غير مدعومة ({extension}). الصيغ المدعومة: xlsx، xls، csv، xml، txt.";
                continue;
            }

            if (file.Length > MaxFileSize)
            {
                zone.Error = $"حجم الملف أكبر من الحد المسموح (25 ميجابايت).";
                continue;
            }

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();

                zone.Analysis = await _import.AnalyzeAsync(bytes, file.FileName, ct);
                if (!zone.Analysis.HasMapping)
                {
                    zone.Error = "لم يتم التعرف على أعمدة كود الموظف ووقت البصمة في هذا الملف. تأكد من أنه ملف بصمات صادر من الجهاز.";
                    continue;
                }

                var token = Guid.NewGuid().ToString("N");
                await System.IO.File.WriteAllBytesAsync(Path.Combine(TempDir, token + ".bin"), bytes, ct);
                zone.Token = token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "تعذّر تحليل ملف البصمة {FileName}", file.FileName);
                zone.Error = "تعذّرت قراءة الملف. تأكد من أنه ملف بصمات صالح غير تالف أو مفتوح في برنامج آخر.";
            }
        }

        return View("Preview", new ImportFilesViewModel { Zones = zones });
    }

    /// <summary>يؤكّد استيراد الملفات الصالحة (كل ملف على حدة) ثم يعالج الحضور للفترة المستخلصة.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        string? entryToken, string? entryName, string? exitToken, string? exitName,
        bool processAttendance = true, CancellationToken ct = default)
    {
        var staged = new[]
        {
            (Title: "ملف الدخول", Token: entryToken, Name: entryName),
            (Title: "ملف الخروج", Token: exitToken, Name: exitName)
        }.Where(s => !string.IsNullOrWhiteSpace(s.Token)).ToList();

        if (staged.Count == 0)
        {
            TempData["Error"] = "انتهت صلاحية المعاينة. الرجاء رفع الملفات مرة أخرى.";
            return RedirectToAction(nameof(Index));
        }

        var messages = new List<string>();
        var errors = new List<string>();
        var importedBatchIds = new List<int>();
        DateTime? periodFrom = null;
        DateTime? periodTo = null;

        foreach (var s in staged)
        {
            var path = Path.Combine(TempDir, s.Token + ".bin");
            if (!System.IO.File.Exists(path))
            {
                errors.Add($"{s.Title}: انتهت صلاحية المعاينة، أعد رفع الملف.");
                continue;
            }

            try
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
                var fileName = string.IsNullOrWhiteSpace(s.Name) ? "attendance-file" : s.Name!;
                var analysis = await _import.AnalyzeAsync(bytes, fileName, ct);

                if (!analysis.HasMapping)
                {
                    errors.Add($"{s.Title} ({fileName}): ملف غير صالح — تعذّر التعرف على الأعمدة المطلوبة.");
                    continue;
                }

                var result = await _import.CommitAsync(analysis, fileName,
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, User.Identity?.Name, ct);

                importedBatchIds.Add(result.ImportBatchId);

                var times = analysis.Rows.Where(r => r.PunchTime.HasValue).Select(r => r.PunchTime!.Value).ToList();
                if (times.Count > 0)
                {
                    var fileFrom = times.Min().Date;
                    var fileTo = times.Max().Date;
                    periodFrom = periodFrom is null || fileFrom < periodFrom ? fileFrom : periodFrom;
                    periodTo = periodTo is null || fileTo > periodTo ? fileTo : periodTo;
                }

                messages.Add(
                    $"{s.Title} ({fileName}): تمت قراءة {analysis.Total} سجل — مقبول {result.Inserted} — " +
                    $"مكرر {result.SkippedDuplicate} — يحتاج مراجعة {analysis.InvalidCount + analysis.UnknownEmployeeCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "تعذّر استيراد {Title} ({FileName})", s.Title, s.Name);
                errors.Add($"{s.Title} ({s.Name}): فشل الاستيراد بسبب خطأ غير متوقع. راجع سجل النظام ثم أعد المحاولة.");
            }
            finally
            {
                try { System.IO.File.Delete(path); } catch { /* ملف مؤقت: تجاهل */ }
            }
        }

        if (messages.Count > 0)
            TempData["Success"] = "نتيجة رفع الملفات — " + string.Join(" | ", messages);
        if (errors.Count > 0)
            TempData["Error"] = string.Join(" | ", errors);

        if (importedBatchIds.Count == 0)
            return RedirectToAction(nameof(Index));

        // ===== معالجة الحضور للفترة التي غطّتها الملفات المستوردة =====
        if (processAttendance && periodFrom.HasValue && periodTo.HasValue)
        {
            var from = periodFrom.Value;
            var to = periodTo.Value;
            if (to < from) (from, to) = (to, from);
            if ((to - from).TotalDays > MaxAutoProcessDays)
            {
                to = from.AddDays(MaxAutoProcessDays);
                TempData["Warning"] =
                    $"فترة البيانات في الملف كبيرة، لذلك تمت معالجة أول {MaxAutoProcessDays} يوم فقط. " +
                    "أعد المعالجة لبقية الفترة من شاشة الحضور اليومي.";
            }

            try
            {
                var processed = await _processing.ProcessAsync(from, to, null, this.CurrentUserName(), ct);
                var text = $"تمت معالجة الحضور من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}: " +
                           $"{processed.EmployeesProcessed} موظف، {processed.DaysProcessed} يوم، " +
                           $"غياب {processed.AbsentDays}، تأخير {processed.LateDays}، إضافي {processed.OvertimeDays}، " +
                           $"يحتاج مراجعة {processed.ReviewCases}.";
                TempData["Success"] = (TempData["Success"] as string) is { Length: > 0 } prior
                    ? prior + " — " + text
                    : text;

                if (processed.Warnings.Count > 0)
                    TempData["Warning"] = string.Join(" | ", processed.Warnings.Take(5));

                return RedirectToAction("Index", "Attendance", new { from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "تعذّرت معالجة الحضور بعد الاستيراد {From}-{To}", from, to);
                TempData["Error"] = (TempData["Error"] as string) is { Length: > 0 } priorErr
                    ? priorErr + " | تعذّرت معالجة الحضور لهذه الفترة، ويمكنك إعادة المعالجة من شاشة الحضور اليومي."
                    : "تم الاستيراد بنجاح، لكن تعذّرت معالجة الحضور لهذه الفترة. أعد المعالجة من شاشة الحضور اليومي.";
            }
        }

        return importedBatchIds.Count == 1
            ? RedirectToAction(nameof(Details), new { id = importedBatchIds[0] })
            : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();
        ViewBag.Punches = await _db.RawPunches.AsNoTracking().Where(p => p.ImportBatchId == id)
            .OrderBy(p => p.PunchTime).Take(100).ToListAsync(ct);
        return View(batch);
    }

    private static ImportFileZoneViewModel BuildZone(
        string key, string title, string icon, string hint, string inputName, IFormFile? file)
        => new()
        {
            Key = key,
            Title = title,
            Icon = icon,
            Hint = hint,
            InputName = inputName,
            Provided = file is { Length: > 0 },
            FileName = file is { Length: > 0 } ? file.FileName : null
        };
}
