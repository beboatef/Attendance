using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManageAttendance")]
public class ShiftsController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public ShiftsController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _db.Shifts.OrderBy(s => s.StartTime).ToListAsync(ct));

    [HttpGet]
    public IActionResult Create() => View("Form", new Shift { OvertimeMultiplier = 1m, GraceMinutes = 0 });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Shift model, CancellationToken ct)
    {
        ValidateShift(model);
        if (!ModelState.IsValid) return View("Form", model);

        _db.Shifts.Add(model);
        await _db.SaveChangesAsync(ct);
        _audit.Log("إضافة وردية", nameof(Shift), model.Id.ToString(), null, model.ShiftName, User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم إضافة الوردية";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var shift = await _db.Shifts.FindAsync(new object?[] { id }, ct);
        if (shift is null) return NotFound();
        return View("Form", shift);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Shift model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        ValidateShift(model);
        if (!ModelState.IsValid) return View("Form", model);

        var shift = await _db.Shifts.FindAsync(new object?[] { id }, ct);
        if (shift is null) return NotFound();

        var old = $"{shift.ShiftName} {shift.StartTime}-{shift.EndTime} سماح {shift.GraceMinutes}";
        shift.ShiftName = model.ShiftName;
        shift.StartTime = model.StartTime;
        shift.EndTime = model.EndTime;
        shift.GraceMinutes = model.GraceMinutes;
        shift.IsOvernight = model.IsOvernight;
        shift.OvertimeMultiplier = model.OvertimeMultiplier <= 0 ? 1m : model.OvertimeMultiplier;
        shift.OvertimeMinMinutes = model.OvertimeMinMinutes;
        shift.IsActive = model.IsActive;
        shift.Notes = model.Notes;
        shift.UpdatedAt = DateTime.UtcNow;
        shift.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        _audit.Log("تعديل وردية", nameof(Shift), shift.Id.ToString(), old,
            $"{shift.ShiftName} {shift.StartTime}-{shift.EndTime} سماح {shift.GraceMinutes}", User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تحديث الوردية";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateShift(Shift model)
    {
        if (model.StartTime == model.EndTime)
            ModelState.AddModelError(nameof(Shift.EndTime), "وقت النهاية يجب أن يختلف عن البداية");
        if (model.GraceMinutes < 0)
            ModelState.AddModelError(nameof(Shift.GraceMinutes), "دقائق السماح لا يمكن أن تكون سالبة");
        if (model.OvertimeMultiplier < 0)
            ModelState.AddModelError(nameof(Shift.OvertimeMultiplier), "معامل الإضافي لا يمكن أن يكون سالبًا");
    }
}
