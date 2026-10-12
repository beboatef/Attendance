using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>سطر راتب موظف داخل كشف شهري.</summary>
public class PayrollItem : BaseEntity
{
    public int PayrollRunId { get; set; }
    public PayrollRun? PayrollRun { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // لقطة من بيانات الموظف وقت الحساب
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;

    public decimal BasicSalary { get; set; }

    public int LateMinutes { get; set; }
    public decimal LateDeduction { get; set; }

    /// <summary>سعر دقيقة العمل المستخدم في الحساب.</summary>
    public decimal MinuteRate { get; set; }

    public decimal ApprovedOvertimeHours { get; set; }
    public decimal OvertimeMultiplier { get; set; } = 1m;
    public decimal OvertimeValue { get; set; }

    public decimal LoansTotal { get; set; }
    public decimal DeductionsTotal { get; set; }
    public decimal OtherDeductions { get; set; }

    public decimal GrossSalary { get; set; }
    public decimal NetSalary { get; set; }

    public PayrollStatus Status { get; set; } = PayrollStatus.Computed;
    public string? Notes { get; set; }

    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Deduction> Deductions { get; set; } = new List<Deduction>();
    public ICollection<OvertimeApproval> OvertimeApprovals { get; set; } = new List<OvertimeApproval>();
}
