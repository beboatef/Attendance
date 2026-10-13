using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Services;

namespace HrPayroll.Web.ViewModels;

public class PayrollIndexViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<PayrollRun> Runs { get; set; } = new();
    public PayrollRun? CurrentRun { get; set; }
    public PayrollReadiness? Readiness { get; set; }
    public List<Domain.Entities.Employee> Employees { get; set; } = new();
    public List<PayrollReportRow> Rows { get; set; } = new();
    public string? Department { get; set; }
    public int? EmployeeId { get; set; }
    public PayrollStatus? Status { get; set; }
    public List<string> Departments { get; set; } = new();

}

public class PayrollDetailsViewModel
{
    public PayrollRun Run { get; set; } = new();
    public List<PayrollReportRow> Rows { get; set; } = new();
    public string? Department { get; set; }
    public List<string> Departments { get; set; } = new();

    public decimal TotalBasic => Rows.Sum(r => r.BasicSalary);
    public decimal TotalOvertime => Rows.Sum(r => r.OvertimeAmount);
    public decimal TotalBonuses => Rows.Sum(r => r.Bonuses + r.Allowances + r.AdditionalDaysAmount);
    public decimal TotalDeductions => Rows.Sum(r => r.TotalDeductions);
    public decimal TotalNet => Rows.Sum(r => r.NetSalary);
    public int TotalEmployees => Rows.Count;
}
