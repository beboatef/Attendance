using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Services;
using HrPayroll.Infrastructure.Services;

namespace HrPayroll.Web.ViewModels;

public class OvertimeIndexViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public List<OvertimeReportRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public Dictionary<int, decimal> HourlyRates { get; set; } = new();

    public int TotalRawMinutes => Rows.Sum(r => r.RawMinutes);
    public int TotalApprovedMinutes => Rows.Sum(r => r.ApprovedMinutes);
    public int PendingCount => Rows.Count(r => r.RawMinutes > 0 && r.ApprovedMinutes == 0);
    public decimal TotalRawHours => Math.Round(TotalRawMinutes / 60m, 2);
    public decimal TotalApprovedHours => Math.Round(TotalApprovedMinutes / 60m, 2);

    /// <summary>
    /// قيمة الإضافي المعتمد بنفس معادلة الرواتب تمامًا (تحويل الدقائق إلى ساعات
    /// ثم × الأجر بالساعة) حتى لا تختلف الأرقام بين شاشة الاعتماد وكشف الراتب.
    /// </summary>
    public decimal EstimatedAmount(decimal hourlyRate, int minutes)
        => PayrollCalculator.OvertimeValueForMinutes(minutes, hourlyRate);
}
