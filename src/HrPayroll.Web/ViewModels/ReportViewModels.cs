using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Services;

namespace HrPayroll.Web.ViewModels;

public class ReportsIndexViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<PayrollRun> PayrollRuns { get; set; } = new();
    public List<DepartmentReportRow> DepartmentRows { get; set; } = new();
    public int DailyRowCount { get; set; }
    public int OvertimeRowCount { get; set; }
    public int AbsenceRowCount { get; set; }
    public int LeaveRowCount { get; set; }
    public int LeaveBalanceCount { get; set; }
    public int DepartmentJobTitleGroups { get; set; }
}

public class DailyReportViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public List<DailyAttendanceReportRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<DepartmentReportRow> DepartmentRows { get; set; } = new();
    public bool GroupByDepartment { get; set; } = true;
}

/// <summary>تقرير الأقسام المستقل: قسم ← مسمى وظيفي ← أسماء الموظفين.</summary>
public class DepartmentReportViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public string? JobTitle { get; set; }
    public List<DepartmentJobTitleReportRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<string> JobTitles { get; set; } = new();

    public int DepartmentCount => Rows.Count;
    public int TotalEmployees => Rows.Sum(r => r.TotalEmployees);
    public int TotalPresent => Rows.Sum(r => r.PresentEmployees);
    public int TotalAbsent => Rows.Sum(r => r.AbsentEmployees);
    public int TotalLate => Rows.Sum(r => r.LateEmployees);
    public int JobTitleGroups => Rows.Sum(r => r.JobTitles.Count);
    public int EmployeeRows => Rows.Sum(r => r.JobTitles.Sum(j => j.Employees.Count));
}

/// <summary>تقرير الإجازات وأرصدة الإجازة السنوية.</summary>
public class LeaveReportViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public LeaveType? LeaveType { get; set; }
    public ApprovalStatus? Status { get; set; }
    public List<LeaveReportRow> Rows { get; set; } = new();
    public List<LeaveBalanceRow> Balances { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();

    public int PendingCount => Rows.Count(r => r.Status == ApprovalStatus.Pending);
    public decimal ApprovedWorkingDays => Rows.Where(r => r.Status == ApprovalStatus.Approved).Sum(r => r.WorkingDays);
    public int EmployeesWithBalance => Balances.Count;
    public decimal TotalRemainingBalance => Balances.Sum(b => b.Balance);
    public decimal TotalUsedBalance => Balances.Sum(b => b.Used);
    public int NegativeBalanceCount => Balances.Count(b => b.Balance < 0);
}

public class EmployeeImportViewModel
{
    public HrPayroll.Infrastructure.Import.EmployeeImportAnalysis Analysis { get; set; } = new();
    public string Token { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public Domain.Enums.EmployeeImportMode Mode { get; set; } = Domain.Enums.EmployeeImportMode.CreateOnly;
    public bool HasMapping => Analysis.Mapping is not null && Analysis.Mapping.IsUsable;
    public List<string> DepartmentNames { get; set; } = new();
}
