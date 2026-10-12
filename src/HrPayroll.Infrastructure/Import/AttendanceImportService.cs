using System.Security.Cryptography;
using System.Text;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Infrastructure.Import;

public sealed class ImportPreviewRow
{
    public int RowNumber { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceEmployeeCode { get; set; }
    public string? Name { get; set; }
    public string? AttType { get; set; }
    public string? RawPunchTime { get; set; }
    public DateTime? PunchTime { get; set; }
    public ImportRowStatus Status { get; set; }
    public string? Message { get; set; }
    public int? EmployeeId { get; set; }
    public string DedupHash { get; set; } = string.Empty;
}

public sealed class ImportAnalysisResult
{
    public AttendanceFileFormat Format { get; set; }
    public ColumnMapping? Mapping { get; set; }
    public bool UsedFallback { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<ImportPreviewRow> Rows { get; set; } = new();
    public List<ImportPreviewRow> Preview { get; set; } = new();
    public string FileHash { get; set; } = string.Empty;

    public int Total => Rows.Count;
    public int SuccessCount => Rows.Count(r => r.Status == ImportRowStatus.Success);
    public int DuplicateCount => Rows.Count(r => r.Status == ImportRowStatus.Duplicate);
    public int InvalidCount => Rows.Count(r => r.Status == ImportRowStatus.InvalidDateTime || r.Status == ImportRowStatus.InvalidRow);
    public int UnknownEmployeeCount => Rows.Count(r => r.Status == ImportRowStatus.UnknownEmployee);
    public bool HasMapping => Mapping is not null && Mapping.IsUsable;
}

public sealed class ImportCommitResult
{
    public int ImportBatchId { get; set; }
    public int Inserted { get; set; }
    public int SkippedDuplicate { get; set; }
    public int Failed { get; set; }
}

/// <summary>خدمة استيراد ملفات البصمة مع التحقق والتكرار ومعاينة قبل التأكيد.</summary>
public class AttendanceImportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AttendanceImportService> _logger;

    public AttendanceImportService(AppDbContext db, ILogger<AttendanceImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public static string ComputeHash(string? deviceId, string code, DateTime time, string? attType)
    {
        var raw = $"{deviceId?.Trim().ToUpperInvariant()}|{code.Trim().ToUpperInvariant()}|{time:yyyy-MM-dd HH:mm:ss}|{attType?.Trim().ToUpperInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    public static string ComputeFileHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>يحلل الملف ويعرض معاينة ونتيجة دون كتابة أي بيانات.</summary>
    public async Task<ImportAnalysisResult> AnalyzeAsync(byte[] bytes, string fileName, CancellationToken ct = default)
    {
        var format = AttendanceFileParser.DetectFormat(bytes);
        var sheet = AttendanceFileParser.Parse(bytes, format);
        var result = new ImportAnalysisResult
        {
            Format = format,
            FileHash = ComputeFileHash(bytes)
        };

        _logger.LogInformation("تحليل ملف {FileName} بصيغة {Format} و {Rows} صف", fileName, format, sheet.Rows.Count);

        var mapping = ColumnMapper.Detect(sheet.Rows);
        if (mapping is null && sheet.Rows.Count > 0)
        {
            mapping = ColumnMapper.Fallback(sheet.Rows);
            result.UsedFallback = true;
        }
        result.Mapping = mapping;

        if (mapping is null || !mapping.IsUsable)
        {
            return result; // لا يمكن المتابعة بدون أعمدة الكود والوقت
        }

        // العناوين
        var headerIdx = Math.Clamp(mapping.HeaderRowIndex, 0, Math.Max(0, sheet.Rows.Count - 1));
        if (sheet.Rows.Count > 0)
            result.Headers = sheet.Rows[headerIdx].Select(x => x ?? string.Empty).ToList();

        // بحث الموظفين بالكود أو كود الجهاز
        var employees = await _db.Employees
            .Where(e => e.EmployeeCode != null)
            .Select(e => new { e.Id, e.EmployeeCode, e.DeviceEmployeeCode })
            .ToListAsync(ct);

        var byDevice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in employees)
        {
            if (!string.IsNullOrWhiteSpace(e.EmployeeCode)) byCode.TryAdd(e.EmployeeCode.Trim(), e.Id);
            if (!string.IsNullOrWhiteSpace(e.DeviceEmployeeCode)) byDevice.TryAdd(e.DeviceEmployeeCode!.Trim(), e.Id);
        }

        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsedHashes = new List<string>();

        for (var r = headerIdx + 1; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            string? Cell(int? idx) => idx.HasValue && idx.Value < row.Length ? row[idx.Value]?.Trim() : null;

            var code = Cell(mapping.EmployeeCode);
            var deviceId = Cell(mapping.DeviceId);
            var name = Cell(mapping.Name);
            var attType = Cell(mapping.AttType);
            var rawTime = Cell(mapping.AttTime);

            // تجاهل الصفوف الفارغة تمامًا
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(rawTime) && string.IsNullOrWhiteSpace(name))
                continue;

            var item = new ImportPreviewRow
            {
                RowNumber = r + 1,
                DeviceId = deviceId,
                DeviceEmployeeCode = code,
                Name = name,
                AttType = attType,
                RawPunchTime = rawTime
            };

            if (string.IsNullOrWhiteSpace(code))
            {
                item.Status = ImportRowStatus.InvalidRow;
                item.Message = "كود الموظف مفقود";
                result.Rows.Add(item);
                continue;
            }

            if (!PunchTimeParser.TryParse(rawTime, out var punchTime, out var err))
            {
                item.Status = ImportRowStatus.InvalidDateTime;
                item.Message = err;
                result.Rows.Add(item);
                continue;
            }

            item.PunchTime = punchTime;

            if (byDevice.TryGetValue(code, out var empId) || byCode.TryGetValue(code, out empId))
                item.EmployeeId = empId;
            else
            {
                item.Status = ImportRowStatus.UnknownEmployee;
                item.Message = "موظف غير مسجل بهذا الكود";
                result.Rows.Add(item);
                continue;
            }

            var hash = ComputeHash(deviceId, code, punchTime, attType);
            item.DedupHash = hash;
            // مكرر داخل الملف نفسه
            if (!seenHashes.Add(hash))
            {
                item.Status = ImportRowStatus.Duplicate;
                item.Message = "مكرر داخل نفس الملف";
                result.Rows.Add(item);
                continue;
            }

            parsedHashes.Add(hash);
            item.Status = ImportRowStatus.Success;
            result.Rows.Add(item);
        }

        // كشف المكرر مقابل قاعدة البيانات (حتى بين ملفات مختلفة)
        var existing = await GetExistingHashesAsync(parsedHashes, ct);
        foreach (var item in result.Rows.Where(x => x.Status == ImportRowStatus.Success))
        {
            if (existing.Contains(item.DedupHash))
            {
                item.Status = ImportRowStatus.Duplicate;
                item.Message = "مستورد مسبقًا في قاعدة البيانات";
            }
        }

        result.Preview = result.Rows.Take(20).ToList();
        return result;
    }

    private async Task<HashSet<string>> GetExistingHashesAsync(List<string> hashes, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int chunkSize = 500;
        for (var i = 0; i < hashes.Count; i += chunkSize)
        {
            var chunk = hashes.Skip(i).Take(chunkSize).ToList();
            var found = await _db.RawPunches
                .Where(p => chunk.Contains(p.DedupHash))
                .Select(p => p.DedupHash)
                .ToListAsync(ct);
            foreach (var h in found) result.Add(h);
        }
        return result;
    }

    /// <summary>يثبّت نتائج التحليل في قاعدة البيانات داخل معاملة واحدة.</summary>
    public async Task<ImportCommitResult> CommitAsync(
        ImportAnalysisResult analysis, string fileName, string? userId, string? userName, CancellationToken ct = default)
    {
        var commit = new ImportCommitResult();
        var rows = analysis.Rows.Where(r => r.Status == ImportRowStatus.Success && r.PunchTime.HasValue).ToList();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var batch = new ImportBatch
        {
            FileName = fileName,
            FileHash = analysis.FileHash,
            ImportedAt = DateTime.UtcNow,
            ImportedByUserId = userId,
            ImportedByName = userName,
            DetectedFormat = analysis.Format.ToString(),
            TotalRows = analysis.Total,
            SuccessRows = 0,
            DuplicateRows = analysis.DuplicateCount,
            InvalidRows = analysis.InvalidCount,
            UnknownEmployeeRows = analysis.UnknownEmployeeCount,
            Notes = analysis.UsedFallback ? "تم استخدام الربط الاحتياطي للأعمدة" : null,
            CreatedBy = userName,
            CreatedAt = DateTime.UtcNow
        };
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        // حماية إضافية من التكرار على مستوى قاعدة البيانات
        var hashes = rows.Select(r => r.DedupHash).Distinct().ToList();
        var existing = await GetExistingHashesAsync(hashes, ct);

        foreach (var row in rows)
        {
            if (existing.Contains(row.DedupHash))
            {
                commit.SkippedDuplicate++;
                continue;
            }

            _db.RawPunches.Add(new RawPunch
            {
                ImportBatchId = batch.Id,
                DeviceId = row.DeviceId,
                DeviceEmployeeCode = row.DeviceEmployeeCode ?? string.Empty,
                RawEmployeeName = row.Name,
                AttType = row.AttType,
                PunchTime = row.PunchTime!.Value,
                RawPunchTimeText = row.RawPunchTime,
                RowNumber = row.RowNumber,
                DedupHash = row.DedupHash,
                EmployeeId = row.EmployeeId,
                IsValid = true,
                CreatedBy = userName,
                CreatedAt = DateTime.UtcNow
            });
            existing.Add(row.DedupHash);
            commit.Inserted++;
        }

        batch.SuccessRows = commit.Inserted;
        batch.DuplicateRows = analysis.DuplicateCount + commit.SkippedDuplicate;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        commit.ImportBatchId = batch.Id;
        _logger.LogInformation("استيراد {File}: {Inserted} جديد، {Skipped} مكرر", fileName, commit.Inserted, commit.SkippedDuplicate);
        return commit;
    }
}
