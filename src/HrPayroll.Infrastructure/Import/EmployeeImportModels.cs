using HrPayroll.Domain.Enums;

namespace HrPayroll.Infrastructure.Import;

/// <summary>حقول ملف الموظفين القابلة للربط بأعمدة Excel.</summary>
public enum EmployeeField
{
    EmployeeCode = 0,
    DeviceEmployeeCode = 1,
    FullName = 2,
    Department = 3,
    JobTitle = 4,
    BasicSalary = 5,
    DailyRate = 6,
    HireDate = 7,
    Phone = 8,
    Email = 9,
    NationalId = 10,
    StandardWorkHours = 11,
    AnnualLeaveEntitlement = 12,
    Notes = 13,
    Ignore = 99
}

/// <summary>خريطة أعمدة ملف الموظفين (رقم العمود لكل حقل).</summary>
public sealed class EmployeeColumnMapping
{
    public int HeaderRowIndex { get; set; }
    public Dictionary<EmployeeField, int> Columns { get; set; } = new();

    public int? IndexOf(EmployeeField f) => Columns.TryGetValue(f, out var i) ? i : null;
    public bool IsUsable => IndexOf(EmployeeField.EmployeeCode).HasValue && IndexOf(EmployeeField.FullName).HasValue;
}

/// <summary>صف موظف بعد التحليل والمعاينة.</summary>
public sealed class EmployeeImportRow
{
    public int RowNumber { get; set; }
    public string? EmployeeCode { get; set; }
    public string? DeviceEmployeeCode { get; set; }
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public decimal? BasicSalary { get; set; }
    public decimal? DailyRate { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public decimal? StandardWorkHours { get; set; }
    public decimal? AnnualLeaveEntitlement { get; set; }
    public string? Notes { get; set; }

    public EmployeeImportRowStatus Status { get; set; } = EmployeeImportRowStatus.New;
    public List<string> Messages { get; set; } = new();
    public int? ExistingEmployeeId { get; set; }
    public string? RawBasicSalary { get; set; }
    public string? RawHireDate { get; set; }

    public bool IsValid => Status is EmployeeImportRowStatus.New
        or EmployeeImportRowStatus.Update or EmployeeImportRowStatus.Unchanged;
}

/// <summary>نتيجة تحليل ملف الموظفين.</summary>
public sealed class EmployeeImportAnalysis
{
    public AttendanceFileFormat Format { get; set; }
    public EmployeeColumnMapping? Mapping { get; set; }
    public bool UsedFallback { get; set; }
    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<EmployeeImportRow> Rows { get; set; } = new();
    public string FileHash { get; set; } = string.Empty;
    public bool AlreadyImported { get; set; }
    public string? PreviousImportSummary { get; set; }

    public int Total => Rows.Count;
    public int NewCount => Rows.Count(r => r.Status == EmployeeImportRowStatus.New);
    public int UpdateCount => Rows.Count(r => r.Status == EmployeeImportRowStatus.Update);
    public int UnchangedCount => Rows.Count(r => r.Status == EmployeeImportRowStatus.Unchanged);
    public int DuplicateCount => Rows.Count(r => r.Status == EmployeeImportRowStatus.Duplicate);
    public int InvalidCount => Rows.Count(r => r.Status == EmployeeImportRowStatus.Invalid);
    public int ValidCount => Rows.Count(r => r.IsValid);
}

public sealed class EmployeeImportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Messages { get; } = new();
}
