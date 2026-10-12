using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManageAttendance")]
public class ImportController : Controller
{
    private const long MaxFileSize = 25 * 1024 * 1024; // 25 MB
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "hrpayroll-import");

    private readonly AttendanceImportService _import;
    private readonly AppDbContext _db;

    public ImportController(AttendanceImportService import, AppDbContext db)
    {
        _import = import;
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Batches = await _db.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.ImportedAt).Take(30).ToListAsync(ct);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "الرجاء اختيار ملف البصمة";
            return RedirectToAction(nameof(Index));
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        ImportAnalysisResult analysis;
        try
        {
            analysis = await _import.AnalyzeAsync(bytes, file.FileName, ct);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "تعذّر قراءة الملف: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }

        if (!analysis.HasMapping)
        {
            TempData["Error"] = "لم يتم التعرف على أعمدة الكود والوقت في الملف. تأكد من وجود عمودي ID و Att.Time";
            return RedirectToAction(nameof(Index));
        }

        Directory.CreateDirectory(TempDir);
        var token = Guid.NewGuid().ToString("N");
        await System.IO.File.WriteAllBytesAsync(Path.Combine(TempDir, token + ".bin"), bytes, ct);

        return View("Preview", new ImportPreviewViewModel
        {
            Token = token,
            FileName = file.FileName,
            FormatName = FormatDisplay(analysis.Format),
            UsedFallback = analysis.UsedFallback,
            HasMapping = analysis.HasMapping,
            Headers = analysis.Headers,
            Analysis = analysis
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(TempDir, token + ".bin");
        if (!System.IO.File.Exists(path))
        {
            TempData["Error"] = "انتهت صلاحية المعاينة. الرجاء رفع الملف مرة أخرى";
            return RedirectToAction(nameof(Index));
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        var analysis = await _import.AnalyzeAsync(bytes, fileName, ct);
        var result = await _import.CommitAsync(analysis, fileName, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            User.Identity?.Name, ct);

        try { System.IO.File.Delete(path); } catch { /* ignore */ }

        TempData["Success"] = $"تم الاستيراد: {result.Inserted} بصمة جديدة، {result.SkippedDuplicate} مكرر تم تجاهله.";
        return RedirectToAction("Details", "Import", new { id = result.ImportBatchId });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();
        ViewBag.Punches = await _db.RawPunches.AsNoTracking().Where(p => p.ImportBatchId == id)
            .OrderBy(p => p.PunchTime).Take(100).ToListAsync(ct);
        return View(batch);
    }

    private static string FormatDisplay(AttendanceFileFormat f) => f switch
    {
        AttendanceFileFormat.Xlsx => "Excel حديث (xlsx)",
        AttendanceFileFormat.Biff8 => "Excel ثنائي قديم (xls)",
        AttendanceFileFormat.SpreadsheetXml => "Excel 2003 XML",
        AttendanceFileFormat.HtmlTable => "جدول HTML",
        AttendanceFileFormat.Csv => "ملف نصي/CSV",
        _ => "غير معروف"
    };
}
