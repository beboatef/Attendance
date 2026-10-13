using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>موظف.</summary>
public class Employee : BaseEntity
{
    /// <summary>كود الموظف الفريد (يُطابق مع كود الجهاز عند الحاجة).</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    /// <summary>الكود كما يظهر في ملفات جهاز البصمة (قد يختلف عن EmployeeCode).</summary>
    public string? DeviceEmployeeCode { get; set; }

    public string FullName { get; set; } = string.Empty;

    // ===== البيانات المرجعية (مع الاحتفاظ بالنص للتوافق وللقطة الرواتب) =====
    public string? JobTitle { get; set; }
    public string? Department { get; set; }

    public int? DepartmentId { get; set; }
    public Department? DepartmentRef { get; set; }

    public int? JobTitleId { get; set; }
    public JobTitle? JobTitleRef { get; set; }

    // ===== الوردية =====
    /// <summary>الوردية الأساسية الحالية (اختيارية: لا يُلزم الموظف بوردية ثابتة).</summary>
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    // ===== الأجر =====
    public decimal BasicSalary { get; set; }

    /// <summary>الأجر اليومي إذا كان له سياسة مستقلة (وإلا يُحسب من الراتب الأساسي / أيام الشهر).</summary>
    public decimal? DailyRate { get; set; }

    /// <summary>ساعات العمل القياسية في اليوم لهذا الموظف (افتراضيًا 8).</summary>
    public decimal StandardWorkHours { get; set; } = 8m;

    // ===== الإجازات =====
    /// <summary>رصيد الإجازة السنوية المستحق في السنة.</summary>
    public decimal AnnualLeaveEntitlement { get; set; }

    /// <summary>الرصيد الحالي المتبقي من الإجازة السنوية.</summary>
    public decimal AnnualLeaveBalance { get; set; }

    // ===== الحالة الوظيفية =====
    public DateTime HireDate { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Active;

    /// <summary>مشتق من الحالة الوظيفية ويُحفظ للتوافق مع الاستعلامات القائمة.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? TerminationDate { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ContactInfo { get; set; }
    public string? NationalId { get; set; }
    public string? Notes { get; set; }

    public ICollection<EmployeeShiftAssignment> ShiftAssignments { get; set; } = new List<EmployeeShiftAssignment>();
    public ICollection<RawPunch> RawPunches { get; set; } = new List<RawPunch>();
    public ICollection<AttendanceEvent> AttendanceEvents { get; set; } = new List<AttendanceEvent>();
    public ICollection<DailyAttendance> DailyAttendances { get; set; } = new List<DailyAttendance>();
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Deduction> Deductions { get; set; } = new List<Deduction>();
    public ICollection<OvertimeApproval> OvertimeApprovals { get; set; } = new List<OvertimeApproval>();
    public ICollection<PayrollItem> PayrollItems { get; set; } = new List<PayrollItem>();
    public ICollection<PayrollAdjustment> Adjustments { get; set; } = new List<PayrollAdjustment>();
    public ICollection<AttendanceCorrection> Corrections { get; set; } = new List<AttendanceCorrection>();
}
