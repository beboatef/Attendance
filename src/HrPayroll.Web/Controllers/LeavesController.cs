using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>إدارة الإجازات وتحويل أيام الغياب إلى إجازة معتمدة.</summary>
[Authorize(Policy = "ManageAttendance")]
public class LeavesController : Controller
{
    private readonly AppDbContext _db;
    private readonly LeaveService _leaves;
    private readonly AuditService _audit;

    public LeavesController(AppDbContext db, LeaveService leaves, AuditService audit)
    {
        _db = db;
        _leaves = leaves;
        _audit = audit;
    }

    public async Task<IActionResult> Index(int? employeeId, LeaveType? leaveType, ApprovalStatus? status,
        DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = _db.LeaveRequests.AsNoTracking().Include(l => l.Employee).AsQueryable();
        if (employeeId.HasValue) q = q.Where(l => l.EmployeeId == employeeId.Value);
        if (leaveType.HasValue) q = q.Where(l => l.LeaveType == leaveType.Value);
        if (status.HasValue) q = q.Where(l => l.Status == status.Value);
        if (from.HasValue) q = q.Where(l => l.EndDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(l => l.StartDate <= to.Value.Date);

        return View(new LeaveIndexViewModel
        {
            Leaves = await q.OrderByDescending(l => l.StartDate).Take(500).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct),
            EmployeeId = employeeId,
            LeaveType = leaveType,
            Status = status,
            From = from,
            To = to
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? employeeId, CancellationToken ct)
        => View("Form", new LeaveFormViewModel
        {
            EmployeeId = employeeId ?? 0,
            Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct)
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeaveFormViewModel vm, CancellationToken ct)
    {
        if (vm.EndDate.Date < vm.StartDate.Date)
            ModelState.AddModelError(nameof(vm.EndDate), "تاريخ النهاية قبل تاريخ البداية");

        if (!await _db.Employees.AnyAsync(e => e.Id == vm.EmployeeId, ct))
            ModelState.AddModelError(nameof(vm.EmployeeId), "الموظف غير موجود");

        var overlaps = await _leaves.FindOverlapsAsync(vm.EmployeeId, vm.StartDate, vm.EndDate, null, ct);
        if (overlaps.Count > 0)
            ModelState.AddModelError(string.Empty,
                "يوجد تداخل مع إجازة أخرى: " +
                string.Join(" / ", overlaps.Select(o => $"{o.StartDate:yyyy-MM-dd} إلى {o.EndDate:yyyy-MM-dd} ({o.LeaveType})")));

        if (!ModelState.IsValid)
        {
            vm.Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
            return View("Form", vm);
        }

        var leave = await _leaves.CreateAsync(new LeaveRequest
        {
            EmployeeId = vm.EmployeeId,
            LeaveType = vm.LeaveType,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            IsPaid = vm.IsPaid && vm.LeaveType != LeaveType.Unpaid,
            Reason = vm.Reason,
            CreatedBy = this.CurrentUserName()
        }, this.CurrentUserName(), allowOverlap: false, ct);

        if (vm.ApproveImmediately)
            await _leaves.ApproveAsync(leave.Id, this.CurrentUserName(), ct);

        TempData["Success"] = $"تم تسجيل الإجازة ({leave.WorkingDays:0.##} يوم عمل)";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var leave = await _db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (leave is null) return NotFound();
        var vm = LeaveFormViewModel.FromEntity(leave);
        vm.Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LeaveFormViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        var leave = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (leave is null) return NotFound();
        if (leave.Status == ApprovalStatus.Approved)
        {
            TempData["Error"] = "لا يمكن تعديل إجازة معتمدة. ارفضها أولًا ثم أعد تسجيلها.";
            return RedirectToAction(nameof(Index));
        }

        if (vm.EndDate.Date < vm.StartDate.Date)
            ModelState.AddModelError(nameof(vm.EndDate), "تاريخ النهاية قبل تاريخ البداية");
        if (!ModelState.IsValid)
        {
            vm.Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
            return View("Form", vm);
        }

        var overlaps = await _leaves.FindOverlapsAsync(vm.EmployeeId, vm.StartDate, vm.EndDate, id, ct);
        if (overlaps.Count > 0)
        {
            ModelState.AddModelError(string.Empty, "يوجد تداخل مع إجازة أخرى لنفس الموظف");
            vm.Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
            return View("Form", vm);
        }

        var old = $"{leave.StartDate:yyyy-MM-dd}|{leave.EndDate:yyyy-MM-dd}|{leave.LeaveType}";
        leave.EmployeeId = vm.EmployeeId;
        leave.LeaveType = vm.LeaveType;
        leave.StartDate = vm.StartDate.Date;
        leave.EndDate = vm.EndDate.Date;
        leave.IsPaid = vm.IsPaid && vm.LeaveType != LeaveType.Unpaid;
        leave.Reason = vm.Reason;
        leave.DaysCount = (leave.EndDate - leave.StartDate).Days + 1;
        leave.WorkingDays = await _leaves.CountWorkingDaysAsync(leave.StartDate, leave.EndDate, ct: ct);
        leave.UpdatedAt = DateTime.UtcNow;
        leave.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعديل إجازة", nameof(LeaveRequest), leave.Id.ToString(), old,
            $"{leave.StartDate:yyyy-MM-dd}|{leave.EndDate:yyyy-MM-dd}|{leave.LeaveType}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم تحديث الإجازة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        await _leaves.ApproveAsync(id, this.CurrentUserName(), ct);
        TempData["Success"] = "تم اعتماد الإجازة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, CancellationToken ct)
    {
        await _leaves.RejectAsync(id, this.CurrentUserName(), ct);
        TempData["Success"] = "تم رفض الإجازة";
        return RedirectToAction(nameof(Index));
    }

    // ================= تحويل الغياب إلى إجازة =================

    [HttpGet]
    public async Task<IActionResult> ConvertAbsence(int? employeeId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = new ConvertAbsenceViewModel
        {
            EmployeeId = employeeId ?? 0,
            From = from?.Date ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            To = to?.Date ?? DateTime.Today,
            Employees = await _db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(ct)
        };

        if (vm.EmployeeId > 0)
            vm.AbsenceDays = await _leaves.GetAbsenceDaysAsync(vm.EmployeeId, vm.From, vm.To, ct);
        else
            vm.AbsenceDays = await _db.DailyAttendances.AsNoTracking().Include(d => d.Employee)
                .Where(d => d.WorkDate >= vm.From && d.WorkDate <= vm.To
                            && (d.Status == AttendanceStatus.Absent || d.Status == AttendanceStatus.UnexcusedAbsence))
                .OrderBy(d => d.WorkDate).Take(300).ToListAsync(ct);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConvertAbsence(ConvertAbsenceViewModel vm, CancellationToken ct)
    {
        if (vm.SelectedDates.Count == 0)
        {
            TempData["Error"] = "يجب تحديد يوم غياب واحد على الأقل للتحويل";
            return RedirectToAction(nameof(ConvertAbsence), new { employeeId = vm.EmployeeId, from = vm.From, to = vm.To });
        }

        try
        {
            var leave = await _leaves.ConvertAbsenceAsync(
                vm.EmployeeId, vm.SelectedDates, vm.LeaveType, vm.Reason, this.CurrentUserName(), ct);
            TempData["Success"] = $"تم تحويل {vm.SelectedDates.Count} يوم غياب إلى إجازة معتمدة ({vm.LeaveType}).";
            return RedirectToAction(nameof(Index), new { employeeId = leave.EmployeeId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(ConvertAbsence), new { employeeId = vm.EmployeeId, from = vm.From, to = vm.To });
        }
    }
}
