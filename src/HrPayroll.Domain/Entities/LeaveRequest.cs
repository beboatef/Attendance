using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>طلب إجازة.</summary>
public class LeaveRequest : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public LeaveType LeaveType { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? Reason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>هل هذه إجازة مدفوعة؟</summary>
    public bool IsPaid { get; set; } = true;
}
