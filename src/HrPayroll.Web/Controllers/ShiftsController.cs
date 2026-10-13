using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>
/// إعداد الورديات. الوردية الأولى محددة مسبقًا 07:00 - 15:00 بسماح 30 دقيقة دخولًا وخروجًا.
/// الورديتان الثانية والثالثة يضبطهما المسؤول: لا يتم اختراع مواعيدهما.
/// </summary>
[Authorize(Policy = "ManageEmployees")]
public class ShiftsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ShiftService _shifts;
    private readonly AuditService _audit;

    public ShiftsController(AppDbContext db, ShiftService shifts, AuditService audit)
    {
        _db = db;
        _shifts = shifts;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _shifts.GetAllAsync(false, ct));

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public IActionResult Create()
    {
        var next = 1;
        try { next = (_db.Shifts.Any() ? _db.Shifts.Max(s => s.ShiftNumber) : 0) + 1; } catch { /* ignore */ }
        return View("Form", new Shift
        {
            ShiftNumber = next,
            StartTime = new TimeSpan(7, 0, 0),
            StandardHours = 8m,
            EntryGraceMinutes = 30,
            ExitGraceMinutes = 30,
            OvertimeMultiplier = 1m,
            IsActive = true,
            WorkingDays = "0,1,2,3,4,6"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Create(Shift model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.ShiftName))
            ModelState.AddModelError(nameof(model.ShiftName), "اسم الوردية مطلوب");
        if (model.StandardHours is <= 0m or > 24m)
            ModelState.AddModelError(nameof(model.StandardHours), "عدد الساعات يجب أن يكون بين 0 و 24");
        if (!ModelState.IsValid) return View("Form", model);

        ShiftService.ApplySchedule(model);
        model.CreatedAt = DateTime.UtcNow;
        model.CreatedBy = this.CurrentUserName();
        _db.Shifts.Add(model);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("إضافة وردية", nameof(Shift), model.Id.ToString(), null,
            $"{model.ShiftName} {model.StartTime:hh\\:mm}-{model.EndTime:hh\\:mm}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم إضافة الوردية";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var shift = await _db.Shifts.FindAsync(new object?[] { id }, ct);
        if (shift is null) return NotFound();
        return View("Form", shift);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(int id, Shift model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (string.IsNullOrWhiteSpace(model.ShiftName))
            ModelState.AddModelError(nameof(model.ShiftName), "اسم الوردية مطلوب");
        if (model.StandardHours is <= 0m or > 24m)
            ModelState.AddModelError(nameof(model.StandardHours), "عدد الساعات يجب أن يكون بين 0 و 24");
        if (!ModelState.IsValid) return View("Form", model);

        var shift = await _db.Shifts.FindAsync(new object?[] { id }, ct);
        if (shift is null) return NotFound();

        var old = $"{shift.ShiftName}|{shift.StartTime:hh\\:mm}-{shift.EndTime:hh\\:mm}|سماح {shift.EntryGraceMinutes}/{shift.ExitGraceMinutes}";

        shift.ShiftName = model.ShiftName.Trim();
        shift.ShiftNumber = model.ShiftNumber;
        shift.StartTime = model.StartTime;
        shift.StandardHours = model.StandardHours;
        shift.EntryGraceMinutes = model.EntryGraceMinutes;
        shift.ExitGraceMinutes = model.ExitGraceMinutes;
        shift.WorkingDays = model.WorkingDays;
        shift.IsActive = model.IsActive;
        shift.Notes = model.Notes;
        shift.OvertimeMultiplier = model.OvertimeMultiplier;
        shift.OvertimeMinMinutes = model.OvertimeMinMinutes;
        ShiftService.ApplySchedule(shift);
        shift.UpdatedAt = DateTime.UtcNow;
        shift.UpdatedBy = this.CurrentUserName();

        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعديل وردية", nameof(Shift), shift.Id.ToString(), old,
            $"{shift.ShiftName}|{shift.StartTime:hh\\:mm}-{shift.EndTime:hh\\:mm}|سماح {shift.EntryGraceMinutes}/{shift.ExitGraceMinutes}",
            this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم تحديث الوردية. أعد معالجة الحضور لتطبيق التغيير على الأيام السابقة.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var shift = await _db.Shifts.FindAsync(new object?[] { id }, ct);
        if (shift is null) return NotFound();
        shift.IsActive = !shift.IsActive;
        shift.UpdatedAt = DateTime.UtcNow;
        shift.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync(shift.IsActive ? "تفعيل وردية" : "تعطيل وردية", nameof(Shift), shift.Id.ToString(),
            null, shift.IsActive.ToString(), this.CurrentUserName(), this.CurrentUserId(), ct);
        TempData["Success"] = shift.IsActive ? "تم تفعيل الوردية" : "تم تعطيل الوردية";
        return RedirectToAction(nameof(Index));
    }
}
