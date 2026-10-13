using HrPayroll.Domain.Entities;
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

public class EmployeeImportViewModel
{
    public HrPayroll.Infrastructure.Import.EmployeeImportAnalysis Analysis { get; set; } = new();
    public string Token { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public Domain.Enums.EmployeeImportMode Mode { get; set; } = Domain.Enums.EmployeeImportMode.CreateOnly;
    public bool HasMapping => Analysis.Mapping is not null && Analysis.Mapping.IsUsable;
    public List<string> DepartmentNames { get; set; } = new();
}
