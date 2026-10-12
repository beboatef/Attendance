using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ViewReports")]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ExcelExportService _excel;

    public ReportsController(AppDbContext db, ExcelExportService excel)
    {
        _db = db; _excel = excel;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ViewBag.From = first.ToString("yyyy-MM-dd");
        ViewBag.To = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.Employees = new SelectList(await _db.Employees.OrderBy(e => e.FullName).ToListAsync(ct), "Id", "FullName");
        ViewBag.Shifts = new SelectList(await _db.Shifts.ToListAsync(ct), "Id", "ShiftName");
        return View();
    }

    private ExportFilter BuildFilter(DateTime? from, DateTime? to, int? employeeId, int? shiftId)
    {
        var f = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var t = (to ?? DateTime.Today).Date;
        if (t < f) (f, t) = (t, f);
        return new ExportFilter { From = f, To = t, EmployeeId = employeeId, ShiftId = shiftId, Year = t.Year, Month = t.Month };
    }

    private IActionResult FileResult(byte[] bytes, string name)
        => File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}_{DateTime.Now:yyyyMMddHHmm}.xlsx");

    public async Task<IActionResult> ExportFull(DateTime? from, DateTime? to, int? employeeId, int? shiftId, CancellationToken ct)
        => FileResult(await _excel.ExportFullAsync(BuildFilter(from, to, employeeId, shiftId), ct), "Attendance_Full_Report");

    public async Task<IActionResult> ExportRaw(DateTime? from, DateTime? to, int? employeeId, CancellationToken ct)
        => FileResult(await _excel.ExportRawAsync(BuildFilter(from, to, employeeId, null), ct), "Raw_Punches");

    public async Task<IActionResult> ExportEntries(DateTime? from, DateTime? to, int? employeeId, int? shiftId, CancellationToken ct)
        => FileResult(await _excel.ExportEventsAsync(BuildFilter(from, to, employeeId, shiftId), AttendanceEventType.Entry, ct), "Entry_Log");

    public async Task<IActionResult> ExportExits(DateTime? from, DateTime? to, int? employeeId, int? shiftId, CancellationToken ct)
        => FileResult(await _excel.ExportEventsAsync(BuildFilter(from, to, employeeId, shiftId), AttendanceEventType.Exit, ct), "Exit_Log");

    public async Task<IActionResult> ExportDaily(DateTime? from, DateTime? to, int? employeeId, int? shiftId, CancellationToken ct)
        => FileResult(await _excel.ExportDailyAsync(BuildFilter(from, to, employeeId, shiftId), ct), "Daily_Attendance");

    public async Task<IActionResult> ExportPayroll(int? year, int? month, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        var run = await _db.PayrollRuns.Where(r => r.Year == y && r.Month == m && r.Status != PayrollStatus.Cancelled)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
        if (run is null)
        {
            TempData["Error"] = "لا يوجد كشف رواتب لهذا الشهر";
            return RedirectToAction(nameof(Index));
        }
        return FileResult(await _excel.ExportPayrollAsync(run.Id, ct), $"Payroll_{y}_{m:00}");
    }
}
