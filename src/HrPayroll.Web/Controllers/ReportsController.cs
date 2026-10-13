using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>التقارير وتصدير Excel و PDF من بيانات قاعدة البيانات الفعلية.</summary>
[Authorize(Policy = "ViewReports")]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel;
    private readonly PdfExportService _pdf;

    public ReportsController(AppDbContext db, ReportService reports, ExcelExportService excel, PdfExportService pdf)
    {
        _db = db;
        _reports = reports;
        _excel = excel;
        _pdf = pdf;
    }

    private AttendanceReportFilter Build(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search)
    {
        var f = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var t = (to ?? DateTime.Today).Date;
        if (t < f) (f, t) = (t, f);
        return new AttendanceReportFilter
        {
            From = f, To = t, Department = department, EmployeeId = employeeId, ShiftId = shiftId,
            Status = status.HasValue && Enum.IsDefined(typeof(AttendanceStatus), status.Value) ? (AttendanceStatus)status.Value : null,
            Search = search
        };
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, CancellationToken ct)
    {
        var filter = Build(from, to, department, employeeId, shiftId, null, null);
        var daily = await _reports.GetDailyAttendanceAsync(filter, ct);

        return View(new ReportsIndexViewModel
        {
            Filter = filter,
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct),
            Shifts = await _db.Shifts.AsNoTracking().OrderBy(s => s.ShiftNumber).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(ct),
            PayrollRuns = await _db.PayrollRuns.AsNoTracking().Where(r => r.Status != PayrollStatus.Cancelled)
                .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Take(24).ToListAsync(ct),
            DepartmentRows = await _reports.GetDepartmentReportAsync(filter, ct),
            DailyRowCount = daily.Count,
            OvertimeRowCount = (await _reports.GetOvertimeAsync(filter, true, ct)).Count,
            AbsenceRowCount = (await _reports.GetAbsenceAndLatenessAsync(filter, ct)).Count
        });
    }

    public async Task<IActionResult> Daily(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search, bool groupByDepartment = true, CancellationToken ct = default)
    {
        var filter = Build(from, to, department, employeeId, shiftId, status, search);
        var rows = await _reports.GetDailyAttendanceAsync(filter, ct);
        return View(new DailyReportViewModel
        {
            Filter = filter,
            Rows = rows,
            DepartmentRows = await _reports.GetDepartmentReportAsync(filter, ct),
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct),
            Shifts = await _db.Shifts.AsNoTracking().OrderBy(s => s.ShiftNumber).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(ct),
            GroupByDepartment = groupByDepartment
        });
    }

    public async Task<IActionResult> Overtime(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, CancellationToken ct)
    {
        var filter = Build(from, to, department, employeeId, shiftId, null, null);
        ViewBag.Filter = filter;
        ViewBag.Employees = await _db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(ct);
        ViewBag.Shifts = await _db.Shifts.AsNoTracking().OrderBy(s => s.ShiftNumber).ToListAsync(ct);
        ViewBag.Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        return View(await _reports.GetOvertimeAsync(filter, false, ct));
    }

    public async Task<IActionResult> Absence(DateTime? from, DateTime? to, string? department, int? employeeId,
        CancellationToken ct)
    {
        var filter = Build(from, to, department, employeeId, null, null, null);
        ViewBag.Filter = filter;
        ViewBag.Employees = await _db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(ct);
        ViewBag.Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        return View(await _reports.GetAbsenceAndLatenessAsync(filter, ct));
    }

    // ===================== التصدير =====================

    private IActionResult Xlsx(byte[] bytes, string name)
        => File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}_{DateTime.Now:yyyyMMddHHmm}.xlsx");

    private IActionResult PdfFile(byte[] bytes, string name)
        => File(bytes, "application/pdf", $"{name}_{DateTime.Now:yyyyMMddHHmm}.pdf");

    public async Task<IActionResult> ExportDailyExcel(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search, CancellationToken ct)
        => Xlsx(await _excel.ExportDailyAttendanceAsync(Build(from, to, department, employeeId, shiftId, status, search), ct), "Daily_Attendance");

    public async Task<IActionResult> ExportDailyPdf(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search, CancellationToken ct)
        => PdfFile(await _pdf.BuildAttendanceReportAsync(Build(from, to, department, employeeId, shiftId, status, search), ct), "Daily_Attendance");

    public async Task<IActionResult> ExportDepartmentExcel(DateTime? from, DateTime? to, string? department,
        int? employeeId, int? shiftId, CancellationToken ct)
        => Xlsx(await _excel.ExportDepartmentReportAsync(Build(from, to, department, employeeId, shiftId, null, null), ct), "Department_Report");

    public async Task<IActionResult> ExportOvertimeExcel(DateTime? from, DateTime? to, string? department,
        int? employeeId, int? shiftId, CancellationToken ct)
        => Xlsx(await _excel.ExportOvertimeAsync(Build(from, to, department, employeeId, shiftId, null, null), ct), "Overtime");

    public async Task<IActionResult> ExportAbsenceExcel(DateTime? from, DateTime? to, string? department,
        int? employeeId, CancellationToken ct)
        => Xlsx(await _excel.ExportAbsenceAsync(Build(from, to, department, employeeId, null, null, null), ct), "Absence");

    public async Task<IActionResult> ExportRawPunches(DateTime? from, DateTime? to, int? employeeId, string? department, CancellationToken ct)
    {
        var filter = Build(from, to, department, employeeId, null, null, null);
        var bytes = await _excel.ExportRawPunchesAsync(new ExportFilter
        {
            From = filter.From, To = filter.To, EmployeeId = filter.EmployeeId, Department = filter.Department
        }, ct);
        return Xlsx(bytes, "Raw_Punches");
    }

    public async Task<IActionResult> ExportEntryExcel(DateTime? from, DateTime? to, string? department,
        int? employeeId, int? shiftId, CancellationToken ct)
        => Xlsx(await _excel.ExportEventsAsync(Build(from, to, department, employeeId, shiftId, null, null), AttendanceEventType.Entry, ct), "Entry");

    public async Task<IActionResult> ExportExitExcel(DateTime? from, DateTime? to, string? department,
        int? employeeId, int? shiftId, CancellationToken ct)
        => Xlsx(await _excel.ExportEventsAsync(Build(from, to, department, employeeId, shiftId, null, null), AttendanceEventType.Exit, ct), "Exit");

    public async Task<IActionResult> ExportFullWorkbook(DateTime? from, DateTime? to, string? department,
        int? employeeId, int? shiftId, CancellationToken ct)
        => Xlsx(await _excel.ExportFullWorkbookAsync(Build(from, to, department, employeeId, shiftId, null, null), ct), "Full_Report");

    public async Task<IActionResult> ExportPayrollExcel(int? runId, CancellationToken ct)
    {
        var run = runId.HasValue
            ? await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId.Value, ct)
            : await _db.PayrollRuns.AsNoTracking().Where(r => r.Status != PayrollStatus.Cancelled)
                .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefaultAsync(ct);

        if (run is null)
        {
            TempData["Error"] = "لا يوجد كشف رواتب للتصدير";
            return RedirectToAction(nameof(Index));
        }
        return Xlsx(await _excel.ExportPayrollAsync(run.Id, ct), $"Payroll_{run.Year}_{run.Month:00}");
    }

    public async Task<IActionResult> ExportPayrollPdf(int? runId, CancellationToken ct)
    {
        var run = runId.HasValue
            ? await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId.Value, ct)
            : await _db.PayrollRuns.AsNoTracking().Where(r => r.Status != PayrollStatus.Cancelled)
                .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefaultAsync(ct);

        if (run is null)
        {
            TempData["Error"] = "لا يوجد كشف رواتب للتصدير";
            return RedirectToAction(nameof(Index));
        }
        return PdfFile(await _pdf.BuildPayrollReportAsync(run.Id, ct), $"Payroll_{run.Year}_{run.Month:00}");
    }
}
