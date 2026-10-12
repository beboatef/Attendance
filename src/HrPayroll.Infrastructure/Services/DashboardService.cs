using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

public sealed class DashboardData
{
    public int ActiveEmployees { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int OnLeave { get; set; }
    public int NeedsReview { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalLoans { get; set; }
    public decimal TotalNetPayroll { get; set; }
    public int PayrollStatus { get; set; }
    public DateTime StatDate { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}

/// <summary>بيانات لوحة التحكم مستخرجة من قاعدة البيانات الفعلية.</summary>
public class DashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardData> GetAsync(int year, int month, DateTime? statDate = null, CancellationToken ct = default)
    {
        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var date = (statDate ?? DateTime.Today).Date;

        var data = new DashboardData { StatDate = date, Year = year, Month = month };

        data.ActiveEmployees = await _db.Employees.CountAsync(e => e.IsActive, ct);

        var dayRows = await _db.DailyAttendances
            .Where(d => d.WorkDate.Date == date)
            .Select(d => new { d.Status, d.PunchReviewStatus, d.ApprovedOvertimeMinutes, d.CalculatedOvertimeMinutes, d.LateMinutes })
            .ToListAsync(ct);

        data.Present = dayRows.Count(d => d.Status is AttendanceStatus.Present or AttendanceStatus.Late);
        data.Absent = dayRows.Count(d => d.Status == AttendanceStatus.Absent);
        data.Late = dayRows.Count(d => d.Status == AttendanceStatus.Late || d.LateMinutes > 0);
        data.OnLeave = dayRows.Count(d => d.Status == AttendanceStatus.Leave);
        data.NeedsReview = dayRows.Count(d => d.PunchReviewStatus == ClassificationStatus.NeedsReview
                                               || d.Status == AttendanceStatus.NeedsReview
                                               || d.Status is AttendanceStatus.MissingCheckIn or AttendanceStatus.MissingCheckOut);

        data.OvertimeHours = await _db.DailyAttendances
            .Where(d => d.WorkDate >= first && d.WorkDate <= last)
            .SumAsync(d => (decimal?)d.ApprovedOvertimeMinutes, ct) is { } mins
            ? Math.Round(mins / 60m, 2)
            : 0m;

        data.TotalDeductions = await _db.Deductions
            .Where(d => d.Year == year && d.Month == month && d.Status == ApprovalStatus.Approved)
            .SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;

        data.TotalLoans = await _db.Loans
            .Where(l => l.DeductionYear == year && l.DeductionMonth == month && l.Status == ApprovalStatus.Approved)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0m;

        var latestRun = await _db.PayrollRuns
            .Where(r => r.Year == year && r.Month == month && r.Status != PayrollStatus.Cancelled)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        if (latestRun is not null)
        {
            data.TotalNetPayroll = latestRun.TotalNet;
            data.PayrollStatus = (int)latestRun.Status;
        }

        return data;
    }
}
