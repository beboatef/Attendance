namespace HrPayroll.Domain.Entities;

/// <summary>
/// البصمة الخام كما وردت من الجهاز دون أي تعديل.
/// يتم الاحتفاظ بالنص الأصلي بالكامل.
/// </summary>
public class RawPunch : BaseEntity
{
    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>رقم الجهاز من الملف.</summary>
    public string? DeviceId { get; set; }

    /// <summary>كود الموظف/الرقم كما ورد من الجهاز.</summary>
    public string DeviceEmployeeCode { get; set; } = string.Empty;

    /// <summary>الاسم كما ورد من الجهاز (لا يُعتمد للمطابقة).</summary>
    public string? RawEmployeeName { get; set; }

    /// <summary>نوع الحركة/البصمة كما ورد (FP / I / O ...).</summary>
    public string? AttType { get; set; }

    /// <summary>الوقت الفعلي للبصمة (محوّل إلى توقيت الشركة المحلي).</summary>
    public DateTime PunchTime { get; set; }

    /// <summary>النص الأصلي لعمود الوقت كما ورد.</summary>
    public string? RawPunchTimeText { get; set; }

    /// <summary>رقم الصف الأصلي في الملف.</summary>
    public int? RowNumber { get; set; }

    /// <summary>بصمة التكرار (Device + Code + Time + Type) لمنع الاستيراد المزدوج.</summary>
    public string DedupHash { get; set; } = string.Empty;

    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public bool IsValid { get; set; } = true;
    public string? ValidationMessage { get; set; }

    public AttendanceEvent? AttendanceEvent { get; set; }
}
