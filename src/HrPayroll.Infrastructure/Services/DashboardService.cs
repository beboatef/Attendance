using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>ملخص قسم في اليوم.</summary>
public sealed class DepartmentAttendanceSummary
{
    public string Department { get; set; } = "غير محدد";
    public int Total { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int OnLeave { get; set; }
    public int RestOrHoliday { get; set; }
    public int NeedsReview { get; set; }
    public decimal PresentRate => Total == 0 ? 0m : Math.Round(Present * 100m / Total, 1);
}

public sealed class PayrollTrendPoint
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalGross { get; set; }
    public int ItemCount { get; set; }
    public string Label => $"{Year}-{Month:00}";
}

public sealed class DashboardData
{
    public DateTime StatDate { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    public int ActiveEmployees { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int EarlyDeparture { get; set; }
    public int OnLeave { get; set; }
    public int RestOrHoliday { get; set; }
    public int MissingEntry { get; set; }
    public int MissingExit { get; set; }
    public int MissingPunches { get; set; }
    public int NeedsReview { get; set; }
    public int AmbiguousShifts { get; set; }
    public int DaysWithoutData { get; set; }

    public int PendingOvertimeApprovals { get; set; }
    public decimal OvertimeHoursMonth { get; set; }
    public decimal ApprovedOvertimeHoursMonth { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalLoans { get; set; }
    public decimal TotalNetPayroll { get; set; }
    public decimal TotalGrossPayroll { get; set; }
    public int PayrollStatus { get; set; }
    public int PayrollItemCount { get; set; }

    public DateTime? LastImportAt { get; set; }
    public string? LastImportFile { get; set; }
    public List<ImportBatch> LatestImports { get; set; } = new();
    public List<DepartmentAttendanceSummary> Departments { get; set; } = new();
    public List<PayrollTrendPoint> PayrollTrend { get; set; } = new();
}

/// <summary>بيانات لوحة التحكم مستخرجة من قاعدة البيانات الفعلية (بدون أي أرقام وهمية).</summary>
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

        var employees = await _db.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.IsActive, e.Department, e.DepartmentRef!.Name })
            .ToListAsync(ct);
        data.ActiveEmployees = employees.Count(e => e.IsActive);

        var dayRows = await _db.DailyAttendances.AsNoTracking()
            .Where(d => d.WorkDate.Date == date)
            .Select(d => new
            {
                d.EmployeeId, d.Status, d.PunchReviewStatus, d.DetectionSource,
                d.ApprovedOvertimeMinutes, d.RawOvertimeMinutes, d.LateMinutes, d.CheckInTime, d.CheckOutTime
            })
            .ToListAsync(ct);

        data.Present = dayRows.Count(d => d.CheckInTime != null);
        data.Absent = dayRows.Count(d => d.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence);
        data.Late = dayRows.Count(d => d.LateMinutes > 0 || d.Status == AttendanceStatus.Late);
        data.EarlyDeparture = dayRows.Count(d => d.Status == AttendanceStatus.EarlyDeparture);
        data.OnLeave = dayRows.Count(d => d.Status is AttendanceStatus.Leave or AttendanceStatus.PaidLeave or AttendanceStatus.UnpaidLeave);
        data.RestOrHoliday = dayRows.Count(d => d.Status is AttendanceStatus.RestDay or AttendanceStatus.Holiday);
        data.MissingEntry = dayRows.Count(d => d.Status == AttendanceStatus.MissingCheckIn || (d.CheckInTime == null && d.CheckOutTime != null));
        data.MissingExit = dayRows.Count(d => d.Status == AttendanceStatus.MissingCheckOut || (d.CheckOutTime == null && d.CheckInTime != null));
        data.MissingPunches = dayRows.Count(d => d.CheckInTime == null ^ d.CheckOutTime == null);
        data.NeedsReview = dayRows.Count(d => d.PunchReviewStatus == ClassificationStatus.NeedsReview
                                              || d.Status == AttendanceStatus.NeedsReview);
        data.AmbiguousShifts = dayRows.Count(d => d.DetectionSource == ShiftDetectionSource.Ambiguous);
        data.DaysWithoutData = data.ActiveEmployees > 0 && dayRows.Count == 0 ? data.ActiveEmployees : 0;

        var monthRows = await _db.DailyAttendances.AsNoTracking()
            .Where(d => d.WorkDate >= first && d.WorkDate <= last)
            .Select(d => new { d.RawOvertimeMinutes, d.ApprovedOvertimeMinutes })
            .ToListAsync(ct);

        data.OvertimeHoursMonth = Math.Round(monthRows.Sum(r => r.RawOvertimeMinutes) / 60m, 2);
        data.ApprovedOvertimeHoursMonth = Math.Round(monthRows.Sum(r => r.ApprovedOvertimeMinutes) / 60m, 2);

        data.PendingOvertimeApprovals = await _db.DailyAttendances.AsNoTracking()
            .CountAsync(d => d.WorkDate >= first && d.WorkDate <= last
                             && d.RawOvertimeMinutes > 0 && d.ApprovedOvertimeMinutes == 0, ct);

        data.TotalDeductions = await _db.Deductions.AsNoTracking()
            .Where(d => d.Year == year && d.Month == month && d.Status == ApprovalStatus.Approved)
            .SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;

        data.TotalLoans = await _db.Loans.AsNoTracking()
            .Where(l => l.DeductionYear == year && l.DeductionMonth == month && l.Status == ApprovalStatus.Approved)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0m;

        var latestRun = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Year == year && r.Month == month && r.Status != PayrollStatus.Cancelled)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        if (latestRun is not null)
        {
            data.TotalNetPayroll = latestRun.TotalNet;
            data.TotalGrossPayroll = latestRun.TotalGross;
            data.PayrollStatus = (int)latestRun.Status;
            data.PayrollItemCount = await _db.PayrollItems.CountAsync(i => i.PayrollRunId == latestRun.Id, ct);
        }

        data.LatestImports = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.DetectedFormat == null || b.DetectedFormat != Import.EmployeeImportService.EmployeeImportMarker)
            .OrderByDescending(b => b.ImportedAt)
            .Take(5)
            .ToListAsync(ct);
        if (data.LatestImports.Count > 0)
        {
            data.LastImportAt = data.LatestImports[0].ImportedAt;
            data.LastImportFile = data.LatestImports[0].FileName;
        }

        // ملخص الأقسام لليوم
        var deptByEmployee = employees
            .GroupBy(e => e.Name ?? e.Department ?? "غير محدد")
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToHashSet());

        var rowsByEmployee = dayRows.GroupBy(d => d.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (dept, ids) in deptByEmployee)
        {
            var summary = new DepartmentAttendanceSummary { Department = dept, Total = ids.Count };
            foreach (var id in ids)
            {
                if (!rowsByEmployee.TryGetValue(id, out var row)) continue;
                switch (row.Status)
                {
                    case AttendanceStatus.Present:
                    case AttendanceStatus.Late:
                    case AttendanceStatus.EarlyDeparture:
                        summary.Present++;
                        if (row.Status == AttendanceStatus.Late) summary.Late++;
                        break;
                    case AttendanceStatus.RestDay:
                    case AttendanceStatus.Holiday:
                        summary.RestOrHoliday++;
                        break;
                    case AttendanceStatus.Leave:
                    case AttendanceStatus.PaidLeave:
                    case AttendanceStatus.UnpaidLeave:
                        summary.OnLeave++;
                        break;
                    case AttendanceStatus.Absent:
                    case AttendanceStatus.UnexcusedAbsence:
                        summary.Absent++;
                        break;
                    default:
                        summary.NeedsReview++;
                        break;
                }
            }
            data.Departments.Add(summary);
        }
        data.Departments = data.Departments.OrderByDescending(d => d.Total).ToList();

        // اتجاه صافي الرواتب لآخر 6 أشهر (من الكشوف غير الملغاة) للرسم البياني.
        var trendFrom = first.AddMonths(-5);
        var allRuns = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Status != PayrollStatus.Cancelled)
            .Select(r => new { r.Id, r.Year, r.Month, r.TotalNet, r.TotalGross })
            .ToListAsync(ct);
        var runs = allRuns
            .Where(r => (r.Year * 12 + r.Month) >= (trendFrom.Year * 12 + trendFrom.Month)
                     && (r.Year * 12 + r.Month) <= (first.Year * 12 + first.Month))
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();
        var runIds = runs.Select(r => r.Id).ToList();
        var counts = runIds.Count == 0 ? new Dictionary<int, int>()
            : await _db.PayrollItems.AsNoTracking()
                .Where(i => runIds.Contains(i.PayrollRunId))
                .GroupBy(i => i.PayrollRunId)
                .Select(g => new { RunId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, ct);
        foreach (var r in runs)
        {
            data.PayrollTrend.Add(new PayrollTrendPoint
            {
                Year = r.Year, Month = r.Month,
                TotalNet = r.TotalNet, TotalGross = r.TotalGross,
                ItemCount = counts.TryGetValue(r.Id, out var c) ? c : 0
            });
        }

        return data;
    }
}
