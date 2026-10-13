using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>
/// شاشة اعتماد الإضافي: تعرض الموظف والتاريخ والوردية ونهاية الوردية والخروج الفعلي
/// والإضافي الخام والمعتمد وسعر الساعة والمبلغ المحسوب — ثم تسمح بالاعتماد أو الرفض.
/// </summary>
[Authorize(Policy = "ManagePayroll")]
public class OvertimeController : Controller
{
    private readonly AppDbContext _db;
    private readonly ReportService _reports;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly ExcelExportService _excel;

    public OvertimeController(AppDbContext db, ReportService reports, SettingsService settings,
        AuditService audit, ExcelExportService excel)
    {
        _db = db;
        _reports = reports;
        _settings = settings;
        _audit = audit;
        _excel = excel;
    }

    private AttendanceReportFilter BuildFilter(DateTime? from, DateTime? to, string? department, int? employeeId, int? shiftId)
    {
        var f = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var t = (to ?? DateTime.Today).Date;
        if (t < f) (f, t) = (t, f);
        return new AttendanceReportFilter { From = f, To = t, Department = department, EmployeeId = employeeId, ShiftId = shiftId };
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, bool onlyWithOvertime = true, CancellationToken ct = default)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId);
        var policy = await _settings.GetPolicyAsync(ct);

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);

        var rates = employees.ToDictionary(
            e => e.Id,
            e => policy.HourlyRateOfSalary(e.BasicSalary, e.DailyRate));

        return View(new OvertimeIndexViewModel
        {
            Filter = filter,
            Rows = await _reports.GetOvertimeAsync(filter, onlyWithOvertime, ct),
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct),
            Shifts = await _db.Shifts.AsNoTracking().OrderBy(s => s.ShiftNumber).ToListAsync(ct),
            Employees = employees,
            HourlyRates = rates
        });
    }

    /// <summary>اعتماد أو تعديل دقائق الإضافي ليوم واحد.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDay(int employeeId, string workDate, int approvedMinutes,
        string? returnUrl, CancellationToken ct)
    {
        if (!DateTime.TryParse(workDate, out var date))
        {
            TempData["Error"] = "تاريخ غير صالح";
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        var daily = await _db.DailyAttendances
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.WorkDate == date.Date, ct);
        if (daily is null)
        {
            TempData["Error"] = "لا يوجد سجل حضور لهذا اليوم";
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        if (await IsPeriodLockedAsync(date, ct))
        {
            TempData["Error"] = $"فترة {date:yyyy-MM} مقفلة براتب معتمد. أعد فتح الكشف أولًا.";
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        approvedMinutes = Math.Max(0, approvedMinutes);
        if (approvedMinutes > daily.RawOvertimeMinutes && daily.RawOvertimeMinutes > 0)
            approvedMinutes = daily.RawOvertimeMinutes;

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        var policy = await _settings.GetPolicyAsync(ct);
        var hourlyRate = employee is null ? 0m : policy.HourlyRateOfSalary(employee.BasicSalary, employee.DailyRate);
        var amount = Math.Round(approvedMinutes / 60m * hourlyRate, 2);

        var old = daily.ApprovedOvertimeMinutes.ToString();
        daily.ApprovedOvertimeMinutes = approvedMinutes;
        daily.PaidOvertimeMinutes = policy.RoundOvertimeMinutes(approvedMinutes);
        daily.UpdatedAt = DateTime.UtcNow;
        daily.UpdatedBy = this.CurrentUserName();

        await _audit.LogAsync(approvedMinutes > 0 ? "اعتماد إضافي" : "إلغاء اعتماد إضافي",
            nameof(Domain.Entities.DailyAttendance), daily.Id.ToString(), old, approvedMinutes.ToString(),
            this.CurrentUserName(), this.CurrentUserId(), ct);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = approvedMinutes > 0
            ? $"تم اعتماد {approvedMinutes} دقيقة إضافي بمبلغ تقديري {amount:N2}."
            : "تم إلغاء اعتماد الإضافي لهذا اليوم.";
        return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveAll(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, string? returnUrl, CancellationToken ct)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId);
        var rows = await _db.DailyAttendances
            .Where(d => d.WorkDate >= filter.From && d.WorkDate <= filter.To && d.RawOvertimeMinutes > 0
                        && d.ApprovedOvertimeMinutes == 0)
            .Where(d => !filter.EmployeeId.HasValue || d.EmployeeId == filter.EmployeeId.Value)
            .Where(d => string.IsNullOrEmpty(filter.Department) || d.Employee!.Department == filter.Department)
            .ToListAsync(ct);

        var lockedMonths = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == PayrollStatus.Locked || r.Status == PayrollStatus.Approved)
            .Select(r => new { r.Year, r.Month }).ToListAsync(ct);
        var lockedSet = lockedMonths.Select(x => (x.Year, x.Month)).ToHashSet();

        var skipped = 0;
        foreach (var d in rows)
        {
            if (lockedSet.Contains((d.WorkDate.Year, d.WorkDate.Month))) { skipped++; continue; }
            d.ApprovedOvertimeMinutes = d.RawOvertimeMinutes;
            d.PaidOvertimeMinutes = d.RawOvertimeMinutes;
            d.UpdatedAt = DateTime.UtcNow;
            d.UpdatedBy = this.CurrentUserName();
        }

        await _audit.LogAsync("اعتماد إضافي جماعي", nameof(Domain.Entities.DailyAttendance), null, null,
            $"اعتماد {rows.Count - skipped} يوم" + (skipped > 0 ? $" (تم تجاوز {skipped} يوم في فترات مقفلة)" : string.Empty),
            this.CurrentUserName(), this.CurrentUserId(), ct);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"تم اعتماد الإضافي الخام لـ {rows.Count - skipped} يوم.";
        if (skipped > 0) TempData["Warning"] = $"تم تجاوز {skipped} يوم لأن فترة رواتبها مقفلة.";
        return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectDay(int employeeId, string workDate, string? returnUrl, CancellationToken ct)
        => await ApproveDay(employeeId, workDate, 0, returnUrl, ct);

    public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, CancellationToken ct)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId);
        var bytes = await _excel.ExportOvertimeAsync(filter, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Overtime_{filter.From:yyyyMMdd}_{filter.To:yyyyMMdd}.xlsx");
    }

    private async Task<bool> IsPeriodLockedAsync(DateTime date, CancellationToken ct)
        => await _db.PayrollRuns.AsNoTracking().AnyAsync(
            r => r.Year == date.Year && r.Month == date.Month
                 && (r.Status == PayrollStatus.Locked || r.Status == PayrollStatus.Approved), ct);
}
