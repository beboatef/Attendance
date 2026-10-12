using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManageLoans")]
public class DeductionsController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public DeductionsController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(int? year, int? month, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var q = _db.Deductions.Include(d => d.Employee).AsNoTracking().Where(d => d.Year == y);
        if (month.HasValue) q = q.Where(d => d.Month == month.Value);
        ViewBag.Year = y; ViewBag.Month = month;
        return View(await q.OrderByDescending(d => d.DeductionDate).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await FillAsync(ct);
        return View("Form", new Deduction { DeductionDate = DateTime.Today, Month = DateTime.Today.Month, Year = DateTime.Today.Year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Deduction model, CancellationToken ct)
    {
        Validate(model);
        if (!ModelState.IsValid) { await FillAsync(ct); return View("Form", model); }
        model.CreatedBy = User.Identity?.Name;
        _db.Deductions.Add(model);
        await _db.SaveChangesAsync(ct);
        _audit.Log("إضافة خصم", nameof(Deduction), model.Id.ToString(), null, $"{model.Amount:0.00} - {model.Reason}", User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تسجيل الخصم";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var d = await _db.Deductions.FindAsync(new object?[] { id }, ct);
        if (d is null) return NotFound();
        if (d.PayrollItemId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل خصم تم تطبيقه في كشف رواتب";
            return RedirectToAction(nameof(Index));
        }
        await FillAsync(ct);
        return View("Form", d);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Deduction model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        Validate(model);
        if (!ModelState.IsValid) { await FillAsync(ct); return View("Form", model); }

        var d = await _db.Deductions.FindAsync(new object?[] { id }, ct);
        if (d is null) return NotFound();
        if (d.PayrollItemId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل خصم مطبّق في كشف رواتب";
            return RedirectToAction(nameof(Index));
        }

        d.EmployeeId = model.EmployeeId;
        d.Amount = model.Amount;
        d.DeductionDate = model.DeductionDate;
        d.Reason = model.Reason;
        d.Month = model.Month;
        d.Year = model.Year;
        d.UpdatedAt = DateTime.UtcNow; d.UpdatedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تحديث الخصم";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var d = await _db.Deductions.FindAsync(new object?[] { id }, ct);
        if (d is null) return NotFound();
        d.Status = d.Status == ApprovalStatus.Approved ? ApprovalStatus.Pending : ApprovalStatus.Approved;
        d.ApprovedBy = User.Identity?.Name;
        d.ApprovedAt = DateTime.UtcNow;
        d.UpdatedAt = DateTime.UtcNow; d.UpdatedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        _audit.Log("تغيير اعتماد خصم", nameof(Deduction), d.Id.ToString(), null, d.Status.ToString(), User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تحديث حالة الخصم";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillAsync(CancellationToken ct)
        => ViewBag.Employees = new SelectList(await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct), "Id", "FullName");

    private void Validate(Deduction model)
    {
        if (model.Amount <= 0) ModelState.AddModelError(nameof(Deduction.Amount), "المبلغ يجب أن يكون أكبر من صفر");
        if (string.IsNullOrWhiteSpace(model.Reason)) ModelState.AddModelError(nameof(Deduction.Reason), "السبب مطلوب");
        if (model.Month is < 1 or > 12) ModelState.AddModelError(nameof(Deduction.Month), "شهر غير صالح");
    }
}
