using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize]
public class AttendanceController : Controller
{
    private readonly AppDbContext _db;
    private readonly AttendanceProcessingService _processing;
    private readonly AuditService _audit;

    public AttendanceController(AppDbContext db, AttendanceProcessingService processing, AuditService audit)
    {
        _db = db;
        _processing = processing;
        _audit = audit;
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? employeeId, int? shiftId, int page = 1, CancellationToken ct = default)
    {
        var f = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var t = (to ?? DateTime.Today).Date;

        var query = _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee).Include(d => d.Shift)
            .Where(d => d.WorkDate >= f && d.WorkDate <= t);
        if (employeeId.HasValue) query = query.Where(d => d.EmployeeId == employeeId);
        if (shiftId.HasValue) query = query.Where(d => d.ShiftId == shiftId);

        const int size = 50;
        ViewBag.From = f; ViewBag.To = t;
        ViewBag.EmployeeId = employeeId; ViewBag.ShiftId = shiftId;
        ViewBag.Employees = new SelectList(await _db.Employees.OrderBy(e => e.FullName).ToListAsync(ct), "Id", "FullName", employeeId);
        ViewBag.Shifts = new SelectList(await _db.Shifts.ToListAsync(ct), "Id", "ShiftName", shiftId);
        ViewBag.Page = page;

        var list = await query.OrderByDescending(d => d.WorkDate).ThenBy(d => d.EmployeeId)
            .Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync(ct);
        return View("Index", list);
    }

    [HttpPost]
    [Authorize(Policy = "ManageAttendance")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(DateTime from, DateTime to, int? employeeId, CancellationToken ct)
    {
        var result = await _processing.ProcessAsync(from, to, employeeId, User.Identity?.Name, ct);
        TempData["Success"] = $"تمت معالجة {result.DaysProcessed} يوم لعدد {result.EmployeesProcessed} موظف، و {result.EventsCreated} حركة مصنفة";
        return RedirectToAction(nameof(Index), new { from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), employeeId });
    }

    public async Task<IActionResult> Exceptions(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var f = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var t = (to ?? DateTime.Today).Date;

        var list = await _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee).Include(d => d.Shift)
            .Where(d => d.WorkDate >= f && d.WorkDate <= t
                        && (d.Status == AttendanceStatus.MissingCheckIn || d.Status == AttendanceStatus.MissingCheckOut
                            || d.Status == AttendanceStatus.NeedsReview
                            || d.PunchReviewStatus == ClassificationStatus.NeedsReview))
            .OrderByDescending(d => d.WorkDate).ToListAsync(ct);

        ViewBag.From = f; ViewBag.To = t;
        return View(list);
    }

    [HttpGet]
    [Authorize(Policy = "ManageAttendance")]
    public async Task<IActionResult> Review(int id, CancellationToken ct)
    {
        var daily = await _db.DailyAttendances.Include(d => d.Employee).Include(d => d.Shift)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (daily is null) return NotFound();
        ViewBag.Events = await _db.AttendanceEvents.Include(e => e.RawPunch)
            .Where(e => e.EmployeeId == daily.EmployeeId && e.WorkDate == daily.WorkDate)
            .OrderBy(e => e.EventTime).ToListAsync(ct);
        return View(daily);
    }

    [HttpPost]
    [Authorize(Policy = "ManageAttendance")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, DateTime? checkIn, DateTime? checkOut, int? status, string? adminNotes, CancellationToken ct)
    {
        var daily = await _db.DailyAttendances.FindAsync(new object?[] { id }, ct);
        if (daily is null) return NotFound();

        var old = $"دخول {daily.CheckInTime} خروج {daily.CheckOutTime} حالة {daily.Status}";
        daily.CheckInTime = checkIn;
        daily.CheckOutTime = checkOut;
        if (status.HasValue && Enum.IsDefined(typeof(AttendanceStatus), status.Value))
            daily.Status = (AttendanceStatus)status.Value;
        daily.AdminNotes = adminNotes;
        daily.IsManualOverride = true;
        daily.PunchReviewStatus = ClassificationStatus.Classified;
        daily.HasIncompletePunches = daily.CheckInTime is null || daily.CheckOutTime is null;
        daily.UpdatedAt = DateTime.UtcNow;
        daily.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        _audit.Log("مراجعة يدوية للحضور", nameof(Domain.Entities.DailyAttendance), daily.Id.ToString(), old,
            $"دخول {daily.CheckInTime} خروج {daily.CheckOutTime} حالة {daily.Status}", User.Identity?.Name);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "تم حفظ المراجعة اليدوية";
        return RedirectToAction(nameof(Index), new { from = daily.WorkDate.ToString("yyyy-MM-dd"), to = daily.WorkDate.ToString("yyyy-MM-dd") });
    }
}
