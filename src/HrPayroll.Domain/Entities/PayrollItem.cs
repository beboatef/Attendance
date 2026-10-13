using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>
/// سطر راتب موظف داخل كشف شهري.
/// يحتوي لقطة كاملة من الأجر والسياسة المستخدمة حتى يبقى الكشف التاريخي قابلًا للتفسير
/// حتى لو تغيّر راتب الموظف أو قسمه لاحقًا.
/// </summary>
public class PayrollItem : BaseEntity
{
    public int PayrollRunId { get; set; }
    public PayrollRun? PayrollRun { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // ===== لقطة من بيانات الموظف وقت الحساب =====
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? JobTitleName { get; set; }

    // ===== لقطة سياسة الأجر المستخدمة =====
    public decimal BasicSalary { get; set; }
    public decimal DailyRate { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal MinuteRate { get; set; }
    public int DailyRateBasisDays { get; set; } = 30;
    public int WorkHoursPerDay { get; set; } = 8;

    // ===== أيام الحضور =====
    public int AttendanceDays { get; set; }
    public int PaidRestDays { get; set; }
    public decimal PaidLeaveDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal AbsenceDays { get; set; }
    public int LateMinutes { get; set; }

    // ===== الإضافي =====
    public int RawOvertimeMinutes { get; set; }
    public decimal RawOvertimeHours { get; set; }
    public decimal ApprovedOvertimeHours { get; set; }
    public decimal PaidOvertimeHours { get; set; }
    public decimal OvertimeMultiplier { get; set; } = 1m;
    public decimal OvertimeAmount { get; set; }

    // ===== الخصومات =====
    public decimal LateDeduction { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal UnpaidLeaveDeduction { get; set; }

    /// <summary>السلف (اسم تاريخي محفوظ للتوافق).</summary>
    public decimal LoansTotal { get; set; }

    public decimal DeductionsTotal { get; set; }

    /// <summary>خصومات أخرى معتمدة من التسويات.</summary>
    public decimal OtherDeductions { get; set; }

    // ===== الإضافات =====
    public decimal Bonuses { get; set; }
    public decimal Allowances { get; set; }
    public decimal AdditionalDaysCount { get; set; }
    public decimal AdditionalDaysAmount { get; set; }

    // ===== الإجماليات =====
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSalary { get; set; }

    /// <summary>الإجمالي قبل الخصومات (يحفظ للتوافق: الراتب + الإضافي + الإضافات).</summary>
    public decimal GrossSalary { get; set; }

    public PayrollStatus Status { get; set; } = PayrollStatus.Computed;
    public string? Notes { get; set; }

    /// <summary>تفاصيل الحساب خطوة بخطوة (JSON) لأغراض المراجعة والتدقيق.</summary>
    public string? CalculationDetails { get; set; }

    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Deduction> Deductions { get; set; } = new List<Deduction>();
    public ICollection<OvertimeApproval> OvertimeApprovals { get; set; } = new List<OvertimeApproval>();
    public ICollection<PayrollAdjustment> Adjustments { get; set; } = new List<PayrollAdjustment>();
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
}
