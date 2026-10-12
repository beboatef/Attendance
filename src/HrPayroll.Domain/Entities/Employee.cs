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
    public string? JobTitle { get; set; }
    public string? Department { get; set; }

    /// <summary>الوردية الأساسية الحالية.</summary>
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public decimal BasicSalary { get; set; }
    public DateTime HireDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? TerminationDate { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
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
}
