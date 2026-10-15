using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>شاشات الحضور اليومي والاستثناءات والتصحيح اليدوي.</summary>
[Authorize(Policy = "ManageAttendance")]
public class AttendanceController : Controller
{
    private readonly AppDbContext _db;
    private readonly AttendanceProcessingService _processing;
    private readonly ReportService _reports;
    private readonly AttendanceCorrectionService _corrections;
    private readonly ExcelExportService _excel;
    private readonly PdfExportService _pdf;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(
        AppDbContext db, AttendanceProcessingService processing, ReportService reports,
        AttendanceCorrectionService corrections, ExcelExportService excel, PdfExportService pdf,
        ILogger<AttendanceController> logger)
    {
        _db = db;
        _processing = processing;
        _reports = reports;
        _corrections = corrections;
        _excel = excel;
        _pdf = pdf;
        _logger = logger;
    }

    private AttendanceReportFilter BuildFilter(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search)
    {
        var f = (from ?? DateTime.Today).Date;
        var t = (to ?? f).Date;
        if (t < f) (f, t) = (t, f);
        return new AttendanceReportFilter
        {
            From = f,
            To = t,
            Department = department,
            EmployeeId = employeeId,
            ShiftId = shiftId,
            Status = status.HasValue && Enum.IsDefined(typeof(AttendanceStatus), status.Value)
                ? (AttendanceStatus)status.Value
                : null,
            Search = search
        };
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search, CancellationToken ct)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId, status, search);
        var vm = new AttendanceIndexViewModel
        {
            Filter = filter,
            Rows = await _reports.GetDailyAttendanceAsync(filter, ct),
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct),
            Shifts = await _db.Shifts.AsNoTracking().OrderBy(s => s.ShiftNumber).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(ct),
            ImportBatches = await _db.ImportBatches.AsNoTracking()
                .Where(b => b.DetectedFormat != EmployeeImportServiceMarker)
                .OrderByDescending(b => b.ImportedAt).Take(20).ToListAsync(ct)
        };
        return View(vm);
    }

    private const string EmployeeImportServiceMarker = HrPayroll.Infrastructure.Import.EmployeeImportService.EmployeeImportMarker;

    /// <summary>الحضور الشهري: مصفوفة موظف × يوم مع مفتاح الحالات بالعربية.</summary>
    public async Task<IActionResult> Monthly(int? year, int? month, string? department, string? search, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        var first = new DateTime(y, m, 1);
        var days = Enumerable.Range(0, DateTime.DaysInMonth(y, m)).Select(i => first.AddDays(i)).ToList();
        var last = days[^1];

        var empQuery = _db.Employees.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(department)) empQuery = empQuery.Where(e => e.Department == department);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            empQuery = empQuery.Where(e => e.FullName.Contains(s) || e.EmployeeCode.Contains(s));
        }
        var employees = await empQuery.OrderBy(e => e.FullName).Take(300).ToListAsync(ct);
        var empIds = employees.Select(e => e.Id).ToList();

        var daily = await _db.DailyAttendances.AsNoTracking()
            .Where(d => empIds.Contains(d.EmployeeId) && d.WorkDate >= first && d.WorkDate <= last)
            .Select(d => new { d.EmployeeId, d.WorkDate, d.Status })
            .ToListAsync(ct);
        var byEmp = daily.GroupBy(d => d.EmployeeId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.WorkDate.Date, x => x.Status));

        var vm = new MonthlyViewModel
        {
            Year = y, Month = m, Department = department, Search = search,
            Days = days,
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)
        };
        foreach (var e in employees)
        {
            var row = new MonthlyRow
            {
                EmployeeId = e.Id, EmployeeCode = e.EmployeeCode, EmployeeName = e.FullName, Department = e.Department
            };
            byEmp.TryGetValue(e.Id, out var map);
            foreach (var d in days)
            {
                if (map != null && map.TryGetValue(d.Date, out var st))
                    row.Cells.Add(new MonthlyCell { Date = d, Status = st, HasRecord = true });
                else
                    row.Cells.Add(new MonthlyCell { Date = d, Status = null, HasRecord = false });
            }
            vm.Rows.Add(row);
        }
        return View(vm);
    }

    /// <summary>الحالات التي تحتاج مراجعة: بصمات ناقصة، وردية غامضة، حالات غير مصنفة.</summary>
    public async Task<IActionResult> Exceptions(DateTime? from, DateTime? to, string? department, CancellationToken ct)
    {
        var filter = BuildFilter(from ?? DateTime.Today.AddDays(-7), to ?? DateTime.Today, department, null, null, null, null);
        var rows = await _reports.GetDailyAttendanceAsync(filter, ct);
        var exceptions = rows.Where(r => r.NeedsReview || r.HasIncompletePunches
                                         || r.DetectionSource == ShiftDetectionSource.Ambiguous
                                         || r.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence)
            .ToList();

        return View(new ExceptionsViewModel
        {
            Filter = filter,
            Rows = exceptions,
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Correct(int id, CancellationToken ct)
    {
        var daily = await _db.DailyAttendances.Include(d => d.Employee).Include(d => d.Shift)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (daily is null) return NotFound();

        return View(new AttendanceCorrectionViewModel
        {
            Daily = daily,
            History = await _corrections.GetHistoryAsync(id, ct),
            CheckIn = daily.CheckInTime,
            CheckOut = daily.CheckOutTime,
            Status = daily.Status,
            ApprovedOvertimeMinutes = daily.ApprovedOvertimeMinutes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Correct(int id, AttendanceCorrectionViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var daily = await _db.DailyAttendances.Include(d => d.Employee).Include(d => d.Shift)
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (daily is null) return NotFound();
            vm.Daily = daily;
            vm.History = await _corrections.GetHistoryAsync(id, ct);
            return View(vm);
        }

        try
        {
            await _corrections.CorrectAsync(new AttendanceCorrectionService.CorrectionRequest
            {
                DailyAttendanceId = id,
                CheckIn = vm.CheckIn,
                CheckOut = vm.CheckOut,
                Status = vm.Status,
                ApprovedOvertimeMinutes = vm.ApprovedOvertimeMinutes,
                Reason = vm.Reason,
                Approve = vm.Approve
            }, this.CurrentUserName(), this.CurrentUserId(), ct);

            TempData["Success"] = "تم تصحيح سجل الحضور وتسجيل التغيير في سجل المراجعة";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        var current = await _db.DailyAttendances.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return RedirectToAction(nameof(Index), new { from = current?.WorkDate.ToString("yyyy-MM-dd"), to = current?.WorkDate.ToString("yyyy-MM-dd") });
    }

    /// <summary>إعادة حساب الحضور لفترة محددة من البصمات الخام المستوردة.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(DateTime? from, DateTime? to, int? employeeId, bool reprocess = true, CancellationToken ct = default)
    {
        var f = (from ?? DateTime.Today.AddDays(-7)).Date;
        var t = (to ?? DateTime.Today).Date;
        if (t < f) (f, t) = (t, f);

        try
        {
            var result = await _processing.ProcessAsync(f, t, employeeId, this.CurrentUserName(), ct);
            TempData["Success"] =
                $"تمت معالجة الحضور: {result.EmployeesProcessed} موظف، {result.DaysProcessed} يوم، {result.EventsCreated} حركة. " +
                $"غياب {result.AbsentDays}، تأخير {result.LateDays}، إضافي {result.OvertimeDays}، راحة {result.RestDays}، عطلات {result.HolidayDays}، إجازات {result.LeaveDays}، " +
                $"يحتاج مراجعة {result.ReviewCases}.";
            if (result.Warnings.Count > 0)
                TempData["Warning"] = string.Join(" | ", result.Warnings.Take(5));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.UserMessage(_logger, "فشلت معالجة الحضور");
        }

        return RedirectToAction(nameof(Index), new { from = f.ToString("yyyy-MM-dd"), to = t.ToString("yyyy-MM-dd") });
    }

    public async Task<IActionResult> ExportExcel(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search, CancellationToken ct)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId, status, search);
        var bytes = await _excel.ExportDailyAttendanceAsync(filter, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Daily_Attendance_{filter.From:yyyyMMdd}_{filter.To:yyyyMMdd}.xlsx");
    }

    public async Task<IActionResult> ExportDepartmentExcel(DateTime? from, DateTime? to, string? department,
        int? employeeId, int? shiftId, CancellationToken ct)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId, null, null);
        var bytes = await _excel.ExportDepartmentReportAsync(filter, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Department_Report_{filter.From:yyyyMMdd}_{filter.To:yyyyMMdd}.xlsx");
    }

    public async Task<IActionResult> ExportPdf(DateTime? from, DateTime? to, string? department, int? employeeId,
        int? shiftId, int? status, string? search, CancellationToken ct)
    {
        var filter = BuildFilter(from, to, department, employeeId, shiftId, status, search);
        var bytes = await _pdf.BuildAttendanceReportAsync(filter, ct);
        return File(bytes, "application/pdf", $"Daily_Attendance_{filter.From:yyyyMMdd}_{filter.To:yyyyMMdd}.pdf");
    }
}
