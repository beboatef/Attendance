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
public class LoansController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public LoansController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(int? year, int? month, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var q = _db.Loans.Include(l => l.Employee).AsNoTracking().Where(l => l.DeductionYear == y);
        if (month.HasValue) q = q.Where(l => l.DeductionMonth == month.Value);
        ViewBag.Year = y; ViewBag.Month = month;
        return View(await q.OrderByDescending(l => l.LoanDate).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await FillAsync(ct);
        return View("Form", new Loan { LoanDate = DateTime.Today, DeductionMonth = DateTime.Today.Month, DeductionYear = DateTime.Today.Year, Installments = 1 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Loan model, CancellationToken ct)
    {
        ValidateLoan(model);
        if (!ModelState.IsValid) { await FillAsync(ct); return View("Form", model); }

        model.CreatedBy = User.Identity?.Name;
        _db.Loans.Add(model);
        await _db.SaveChangesAsync(ct);
        _audit.Log("إضافة سلفة", nameof(Loan), model.Id.ToString(), null, model.Amount.ToString("0.00"), User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تسجيل السلفة";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var loan = await _db.Loans.FindAsync(new object?[] { id }, ct);
        if (loan is null) return NotFound();
        if (loan.PayrollItemId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل سلفة تم خصمها في كشف رواتب معتمد. ألغِ الاعتماد أولًا.";
            return RedirectToAction(nameof(Index));
        }
        await FillAsync(ct);
        return View("Form", loan);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Loan model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        ValidateLoan(model);
        if (!ModelState.IsValid) { await FillAsync(ct); return View("Form", model); }

        var loan = await _db.Loans.FindAsync(new object?[] { id }, ct);
        if (loan is null) return NotFound();
        if (loan.PayrollItemId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل سلفة تم خصمها في كشف رواتب";
            return RedirectToAction(nameof(Index));
        }

        loan.EmployeeId = model.EmployeeId;
        loan.Amount = model.Amount;
        loan.LoanDate = model.LoanDate;
        loan.DeductionMonth = model.DeductionMonth;
        loan.DeductionYear = model.DeductionYear;
        loan.Installments = model.Installments;
        loan.Notes = model.Notes;
        loan.UpdatedAt = DateTime.UtcNow; loan.UpdatedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تحديث السلفة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var loan = await _db.Loans.FindAsync(new object?[] { id }, ct);
        if (loan is null) return NotFound();
        loan.Status = loan.Status == ApprovalStatus.Approved ? ApprovalStatus.Pending : ApprovalStatus.Approved;
        loan.UpdatedAt = DateTime.UtcNow; loan.UpdatedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        _audit.Log("تغيير حالة اعتماد سلفة", nameof(Loan), loan.Id.ToString(), null, loan.Status.ToString(), User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم تحديث حالة السلفة";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillAsync(CancellationToken ct)
        => ViewBag.Employees = new SelectList(await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct), "Id", "FullName");

    private void ValidateLoan(Loan model)
    {
        if (model.Amount <= 0) ModelState.AddModelError(nameof(Loan.Amount), "المبلغ يجب أن يكون أكبر من صفر");
        if (model.DeductionMonth is < 1 or > 12) ModelState.AddModelError(nameof(Loan.DeductionMonth), "شهر غير صالح");
        if (model.Installments < 1) ModelState.AddModelError(nameof(Loan.Installments), "عدد الأقساط غير صالح");
    }
}
