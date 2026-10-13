using System.Security.Cryptography;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Infrastructure.Import;

/// <summary>ملخص صف للعرض.</summary>
public sealed class EmployeeImportPreviewRow
{
    public int RowNumber { get; set; }
    public string? Message { get; set; }
    public EmployeeImportRowStatus Status { get; set; }
}

/// <summary>
/// استيراد قائمة الموظفين والرواتب من ملف Excel:
/// تحليل ← معاينة وتحقق ← ربط أعمدة ← تأكيد.
/// لا يستبدل بيانات موظف موجود إلا بطلب صريح، ويسجل نتيجة الاستيراد.
/// </summary>
public class EmployeeImportService
{
    public const string EmployeeImportMarker = "Employees";

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ILogger<EmployeeImportService> _logger;

    public EmployeeImportService(AppDbContext db, AuditService audit, ILogger<EmployeeImportService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public static string ComputeFileHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>يحلل الملف ويعرض معاينة كاملة دون كتابة أي بيانات.</summary>
    public async Task<EmployeeImportAnalysis> AnalyzeAsync(byte[] bytes, string fileName, CancellationToken ct = default)
    {
        var format = AttendanceFileParser.DetectFormat(bytes);
        var sheet = AttendanceFileParser.Parse(bytes, format);

        var result = new EmployeeImportAnalysis
        {
            Format = format,
            FileHash = ComputeFileHash(bytes)
        };

        var mapping = EmployeeColumnMapper.Detect(sheet.Rows);
        result.UsedFallback = mapping is null;
        result.Mapping = mapping;

        if (mapping is null || !mapping.IsUsable)
        {
            _logger.LogWarning("تعذّر ربط أعمدة ملف الموظفين {File}", fileName);
            return result;
        }

        var headerIdx = Math.Clamp(mapping.HeaderRowIndex, 0, Math.Max(0, sheet.Rows.Count - 1));
        result.Headers = sheet.Rows.Count > 0
            ? sheet.Rows[headerIdx].Select(x => x ?? string.Empty).ToArray()
            : Array.Empty<string>();

        // كشف إعادة استيراد نفس الملف
        var previous = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.DetectedFormat == EmployeeImportMarker && b.FileHash == result.FileHash)
            .OrderByDescending(b => b.ImportedAt)
            .FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            result.AlreadyImported = true;
            result.PreviousImportSummary =
                $"سُجّل استيراد لنفس الملف بتاريخ {previous.ImportedAt.ToLocalTime():yyyy-MM-dd HH:mm} بواسطة {previous.ImportedByName ?? "غير معروف"} " +
                $"(جديد: {previous.SuccessRows}، محدَّث: {previous.UnknownEmployeeRows}، متجاهَل: {previous.InvalidRows}).";
        }

        // بيانات مرجعية موجودة
        var knownCodes = await _db.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.EmployeeCode, e.DeviceEmployeeCode, e.FullName, e.BasicSalary, e.Department, e.JobTitle })
            .ToListAsync(ct);
        var byCode = new Dictionary<string, (int Id, string Name, decimal Salary, string? Dept, string? Title)>(StringComparer.OrdinalIgnoreCase);
        var byDevice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in knownCodes)
        {
            if (!string.IsNullOrWhiteSpace(e.EmployeeCode))
                byCode.TryAdd(e.EmployeeCode.Trim(), (e.Id, e.FullName, e.BasicSalary, e.Department, e.JobTitle));
            if (!string.IsNullOrWhiteSpace(e.DeviceEmployeeCode))
                byDevice.TryAdd(e.DeviceEmployeeCode!.Trim(), e.Id);
        }

        var departments = await _db.Departments.AsNoTracking().Select(d => d.Name).ToListAsync(ct);
        var jobTitles = await _db.JobTitles.AsNoTracking().Select(j => j.Name).ToListAsync(ct);
        var knownDepartments = departments.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownJobTitles = jobTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDeviceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var r = headerIdx + 1; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            string? Cell(EmployeeField f)
            {
                var idx = result.Mapping!.IndexOf(f);
                return idx.HasValue && idx.Value < row.Length ? row[idx.Value]?.Trim() : null;
            }

            var code = Cell(EmployeeField.EmployeeCode);
            var name = Cell(EmployeeField.FullName);
            var rawSalary = Cell(EmployeeField.BasicSalary);
            var rawHire = Cell(EmployeeField.HireDate);

            // تجاهل الصفوف الفارغة تمامًا
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(rawSalary))
                continue;

            var item = new EmployeeImportRow
            {
                RowNumber = r + 1,
                EmployeeCode = code,
                DeviceEmployeeCode = Cell(EmployeeField.DeviceEmployeeCode),
                FullName = name,
                Department = Cell(EmployeeField.Department),
                JobTitle = Cell(EmployeeField.JobTitle),
                Phone = Cell(EmployeeField.Phone),
                Email = Cell(EmployeeField.Email),
                NationalId = Cell(EmployeeField.NationalId),
                Notes = Cell(EmployeeField.Notes),
                RawBasicSalary = rawSalary,
                RawHireDate = rawHire,
                DailyRate = CellValueParser.TryParseMoney(Cell(EmployeeField.DailyRate)),
                StandardWorkHours = CellValueParser.TryParseMoney(Cell(EmployeeField.StandardWorkHours)),
                AnnualLeaveEntitlement = CellValueParser.TryParseMoney(Cell(EmployeeField.AnnualLeaveEntitlement))
            };

            var invalid = false;

            if (string.IsNullOrWhiteSpace(code))
            {
                item.Messages.Add("كود الموظف مفقود");
                invalid = true;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                item.Messages.Add("اسم الموظف مفقود");
                invalid = true;
            }

            if (string.IsNullOrWhiteSpace(rawSalary))
            {
                item.Messages.Add("الراتب الأساسي مفقود");
                invalid = true;
            }
            else
            {
                item.BasicSalary = CellValueParser.TryParseMoney(rawSalary);
                if (item.BasicSalary is null)
                {
                    item.Messages.Add($"قيمة راتب غير صالحة: {rawSalary}");
                    invalid = true;
                }
                else if (item.BasicSalary <= 0m)
                {
                    item.Messages.Add($"الراتب يجب أن يكون أكبر من صفر: {rawSalary}");
                    invalid = true;
                }
            }

            if (string.IsNullOrWhiteSpace(rawHire))
            {
                item.HireDate = DateTime.Today;
                item.Messages.Add("تاريخ التعيين مفقود — سيتم استخدام تاريخ اليوم");
            }
            else if (PunchTimeParser.TryParse(rawHire, out var hire, out _))
            {
                item.HireDate = hire.Date;
                if (hire.Date > DateTime.Today.AddDays(1))
                    item.Messages.Add("تاريخ التعيين في المستقبل");
            }
            else
            {
                item.Messages.Add($"تاريخ تعيين غير صالح: {rawHire}");
                invalid = true;
            }

            if (item.DailyRate is < 0m) { item.Messages.Add("الأجر اليومي غير صالح"); invalid = true; }
            if (item.StandardWorkHours is <= 0m or > 24m) { item.Messages.Add("ساعات العمل اليومية غير صالحة"); invalid = true; }

            if (!string.IsNullOrWhiteSpace(item.Department) && !knownDepartments.Contains(item.Department!))
                item.Messages.Add($"قسم غير مسجل مسبقًا: {item.Department} (سيتم إنشاؤه)");

            if (!string.IsNullOrWhiteSpace(item.JobTitle) && !knownJobTitles.Contains(item.JobTitle!))
                item.Messages.Add($"مسمى وظيفي غير مسجل مسبقًا: {item.JobTitle} (سيتم إنشاؤه)");

            if (invalid)
            {
                item.Status = EmployeeImportRowStatus.Invalid;
                result.Rows.Add(item);
                continue;
            }

            // تكرار داخل نفس الملف
            if (!seenCodes.Add(code!.Trim()))
            {
                item.Status = EmployeeImportRowStatus.Duplicate;
                item.Messages.Add("كود الموظف مكرر داخل نفس الملف");
                result.Rows.Add(item);
                continue;
            }

            var device = string.IsNullOrWhiteSpace(item.DeviceEmployeeCode) ? code!.Trim() : item.DeviceEmployeeCode!.Trim();
            if (!seenDeviceCodes.Add(device))
            {
                item.Status = EmployeeImportRowStatus.Duplicate;
                item.Messages.Add("كود الجهاز مكرر داخل نفس الملف");
                result.Rows.Add(item);
                continue;
            }

            // كود جهاز مرتبط بموظف آخر
            if (byDevice.TryGetValue(device, out var otherId) &&
                (!byCode.TryGetValue(code!.Trim(), out var self) || self.Id != otherId))
            {
                item.Status = EmployeeImportRowStatus.Duplicate;
                item.Messages.Add("كود الجهاز مسجل بالفعل لموظف آخر");
                result.Rows.Add(item);
                continue;
            }

            if (byCode.TryGetValue(code!.Trim(), out var existing))
            {
                item.ExistingEmployeeId = existing.Id;
                var changed = existing.Name.Trim() != name!.Trim()
                              || existing.Salary != item.BasicSalary
                              || (existing.Dept ?? string.Empty) != (item.Department ?? string.Empty)
                              || (existing.Title ?? string.Empty) != (item.JobTitle ?? string.Empty);
                item.Status = changed ? EmployeeImportRowStatus.Update : EmployeeImportRowStatus.Unchanged;
                item.Messages.Add(changed ? "موظف موجود — ستُحدَّث بياناته" : "موظف موجود بنفس البيانات");
            }
            else
            {
                item.Status = EmployeeImportRowStatus.New;
                item.Messages.Add("موظف جديد");
            }

            result.Rows.Add(item);
        }

        return result;
    }

    /// <summary>يثبّت نتائج المعاينة في قاعدة البيانات داخل معاملة واحدة.</summary>
    public async Task<EmployeeImportResult> CommitAsync(
        EmployeeImportAnalysis analysis, EmployeeImportMode mode, string fileName,
        string? userId, string? userName, CancellationToken ct = default)
    {
        var result = new EmployeeImportResult();
        var rows = analysis.Rows.Where(r => r.IsValid).ToList();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // أقسام ومسميات جديدة تُنشأ تلقائيًا للحفاظ على تناسق البيانات المرجعية
        var departments = await _db.Departments.ToListAsync(ct);
        var jobTitles = await _db.JobTitles.ToListAsync(ct);

        Department? EnsureDepartment(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var d = departments.FirstOrDefault(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (d is not null) return d;
            d = new Department { Name = name.Trim(), IsActive = true, CreatedBy = userName, CreatedAt = DateTime.UtcNow };
            _db.Departments.Add(d);
            departments.Add(d);
            result.Messages.Add($"تم إنشاء قسم جديد: {d.Name}");
            return d;
        }

        JobTitle? EnsureJobTitle(string? name, Department? department)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var j = jobTitles.FirstOrDefault(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (j is not null) return j;
            j = new JobTitle
            {
                Name = name.Trim(),
                // يُربط بالكائن (Navigation) لا بالمعرّف، لأن القسم قد يكون جديدًا ولم يحصل على معرّفه بعد
                Department = department,
                IsActive = true,
                CreatedBy = userName,
                CreatedAt = DateTime.UtcNow
            };
            _db.JobTitles.Add(j);
            jobTitles.Add(j);
            result.Messages.Add($"تم إنشاء مسمى وظيفي جديد: {j.Name}");
            return j;
        }

        foreach (var row in rows)
        {
            var departmentRef = EnsureDepartment(row.Department);
            var jobTitleRef = EnsureJobTitle(row.JobTitle, departmentRef);

            if (row.Status == EmployeeImportRowStatus.New)
            {
                var emp = new Employee
                {
                    EmployeeCode = row.EmployeeCode!.Trim(),
                    DeviceEmployeeCode = string.IsNullOrWhiteSpace(row.DeviceEmployeeCode)
                        ? row.EmployeeCode!.Trim()
                        : row.DeviceEmployeeCode!.Trim(),
                    FullName = row.FullName!.Trim(),
                    // الربط بالكائنات معًا يضمن أن الأقسام/المسميات المنشأة في نفس الحفظ تُربط صحيحًا
                    Department = departmentRef?.Name,
                    DepartmentRef = departmentRef,
                    DepartmentId = departmentRef is { Id: > 0 } ? departmentRef.Id : null,
                    JobTitle = jobTitleRef?.Name,
                    JobTitleRef = jobTitleRef,
                    JobTitleId = jobTitleRef is { Id: > 0 } ? jobTitleRef.Id : null,
                    BasicSalary = row.BasicSalary ?? 0m,
                    DailyRate = row.DailyRate,
                    StandardWorkHours = row.StandardWorkHours is > 0m ? row.StandardWorkHours.Value : 8m,
                    AnnualLeaveEntitlement = row.AnnualLeaveEntitlement ?? 0m,
                    AnnualLeaveBalance = row.AnnualLeaveEntitlement ?? 0m,
                    HireDate = row.HireDate ?? DateTime.Today,
                    EmploymentStatus = EmploymentStatus.Active,
                    IsActive = true,
                    Phone = row.Phone,
                    Email = row.Email,
                    NationalId = row.NationalId,
                    Notes = row.Notes,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userName
                };
                _db.Employees.Add(emp);
                result.Created++;
                continue;
            }

            if (row.Status == EmployeeImportRowStatus.Update)
            {
                if (mode != EmployeeImportMode.CreateAndUpdate)
                {
                    result.Skipped++;
                    result.Messages.Add($"الموظف {row.EmployeeCode}: موجود مسبقًا ولم يُحدَّث (وضع الاستيراد = إضافة الجديد فقط).");
                    continue;
                }

                var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.ExistingEmployeeId, ct);
                if (emp is null) { result.Skipped++; continue; }

                var before = $"{emp.FullName}|{emp.BasicSalary}|{emp.Department}|{emp.JobTitle}";
                emp.FullName = row.FullName!.Trim();
                emp.DeviceEmployeeCode = string.IsNullOrWhiteSpace(row.DeviceEmployeeCode)
                    ? emp.DeviceEmployeeCode
                    : row.DeviceEmployeeCode!.Trim();
                emp.Department = departmentRef?.Name ?? emp.Department;
                if (departmentRef is not null)
                {
                    emp.DepartmentRef = departmentRef;
                    if (departmentRef.Id > 0) emp.DepartmentId = departmentRef.Id;
                }
                emp.JobTitle = jobTitleRef?.Name ?? emp.JobTitle;
                if (jobTitleRef is not null)
                {
                    emp.JobTitleRef = jobTitleRef;
                    if (jobTitleRef.Id > 0) emp.JobTitleId = jobTitleRef.Id;
                }
                emp.BasicSalary = row.BasicSalary ?? emp.BasicSalary;
                if (row.DailyRate.HasValue) emp.DailyRate = row.DailyRate;
                if (row.StandardWorkHours is > 0m) emp.StandardWorkHours = row.StandardWorkHours.Value;
                if (row.AnnualLeaveEntitlement.HasValue) emp.AnnualLeaveEntitlement = row.AnnualLeaveEntitlement.Value;
                if (row.HireDate.HasValue) emp.HireDate = row.HireDate.Value;
                if (!string.IsNullOrWhiteSpace(row.Phone)) emp.Phone = row.Phone;
                if (!string.IsNullOrWhiteSpace(row.Email)) emp.Email = row.Email;
                if (!string.IsNullOrWhiteSpace(row.NationalId)) emp.NationalId = row.NationalId;
                if (!string.IsNullOrWhiteSpace(row.Notes)) emp.Notes = row.Notes;
                emp.UpdatedAt = DateTime.UtcNow;
                emp.UpdatedBy = userName;

                await _audit.LogAsync("تحديث موظف من ملف Excel", nameof(Employee), emp.Id.ToString(),
                    before, $"{emp.FullName}|{emp.BasicSalary}|{emp.Department}|{emp.JobTitle}", userName, userId, ct);
                result.Updated++;
                continue;
            }

            result.Skipped++;
        }

        var batch = new ImportBatch
        {
            FileName = fileName,
            FileHash = analysis.FileHash,
            ImportedAt = DateTime.UtcNow,
            ImportedByUserId = userId,
            ImportedByName = userName,
            DetectedFormat = EmployeeImportMarker,
            TotalRows = analysis.Total,
            SuccessRows = result.Created,
            // يُستخدم الحقل لحفظ عدد الصفوف المحدَّثة في استيراد الموظفين
            UnknownEmployeeRows = result.Updated,
            InvalidRows = analysis.InvalidCount + result.Skipped,
            DuplicateRows = analysis.DuplicateCount,
            Notes = $"استيراد موظفين ({mode}): جديد {result.Created}، محدَّث {result.Updated}، متجاهَل {result.Skipped}، " +
                    $"مكرر {analysis.DuplicateCount}، غير صالح {analysis.InvalidCount}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userName
        };
        _db.ImportBatches.Add(batch);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("استيراد موظفين {File}: {Created} جديد، {Updated} محدَّث", fileName, result.Created, result.Updated);
        return result;
    }
}
