using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>إدارة بيانات الأقسام المرجعية.</summary>
[Authorize(Policy = "ManageEmployees")]
public class DepartmentsController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public DepartmentsController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(string? search, bool? isActive, CancellationToken ct)
    {
        var q = _db.Departments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(d => d.Name.Contains(s) || (d.Code != null && d.Code.Contains(s)));
        }
        if (isActive.HasValue) q = q.Where(d => d.IsActive == isActive.Value);

        ViewBag.Search = search;
        ViewBag.IsActive = isActive;
        var departments = await q.OrderBy(d => d.Name).ToListAsync(ct);
        ViewBag.EmployeeCounts = await _db.Employees.AsNoTracking()
            .Where(e => e.DepartmentId != null)
            .GroupBy(e => e.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);
        ViewBag.JobTitleCounts = await _db.JobTitles.AsNoTracking()
            .Where(j => j.DepartmentId != null)
            .GroupBy(j => j.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);
        return View(departments);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new DepartmentFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentFormViewModel vm, CancellationToken ct)
    {
        if (await _db.Departments.AnyAsync(d => d.Name == vm.Name.Trim(), ct))
            ModelState.AddModelError(nameof(vm.Name), "يوجد قسم بنفس الاسم");

        if (!ModelState.IsValid) return View("Form", vm);

        var dept = new Department
        {
            Name = vm.Name.Trim(),
            Code = vm.Code?.Trim(),
            ManagerName = vm.ManagerName,
            IsActive = vm.IsActive,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = this.CurrentUserName()
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("إضافة قسم", nameof(Department), dept.Id.ToString(), null, dept.Name, this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم إضافة القسم بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var dept = await _db.Departments.FindAsync(new object?[] { id }, ct);
        if (dept is null) return NotFound();
        return View("Form", new DepartmentFormViewModel
        {
            Id = dept.Id, Name = dept.Name, Code = dept.Code, ManagerName = dept.ManagerName,
            IsActive = dept.IsActive, Notes = dept.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DepartmentFormViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        if (await _db.Departments.AnyAsync(d => d.Name == vm.Name.Trim() && d.Id != id, ct))
            ModelState.AddModelError(nameof(vm.Name), "يوجد قسم بنفس الاسم");
        if (!ModelState.IsValid) return View("Form", vm);

        var dept = await _db.Departments.FindAsync(new object?[] { id }, ct);
        if (dept is null) return NotFound();

        var old = $"{dept.Name}|{dept.Code}|{dept.IsActive}";
        dept.Name = vm.Name.Trim();
        dept.Code = vm.Code?.Trim();
        dept.ManagerName = vm.ManagerName;
        dept.IsActive = vm.IsActive;
        dept.Notes = vm.Notes;
        dept.UpdatedAt = DateTime.UtcNow;
        dept.UpdatedBy = this.CurrentUserName();
        await SyncEmployeeDepartmentNameAsync(dept, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعديل قسم", nameof(Department), dept.Id.ToString(), old,
            $"{dept.Name}|{dept.Code}|{dept.IsActive}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم تحديث القسم";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var dept = await _db.Departments.FindAsync(new object?[] { id }, ct);
        if (dept is null) return NotFound();
        dept.IsActive = !dept.IsActive;
        dept.UpdatedAt = DateTime.UtcNow;
        dept.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync(dept.IsActive ? "تفعيل قسم" : "تعطيل قسم", nameof(Department), dept.Id.ToString(),
            null, dept.IsActive.ToString(), this.CurrentUserName(), this.CurrentUserId(), ct);
        TempData["Success"] = dept.IsActive ? "تم تفعيل القسم" : "تم تعطيل القسم";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>يحدّث النص المخزّن على الموظفين عند إعادة تسمية القسم.</summary>
    private async Task SyncEmployeeDepartmentNameAsync(Department dept, CancellationToken ct)
    {
        var employees = await _db.Employees.Where(e => e.DepartmentId == dept.Id).ToListAsync(ct);
        foreach (var e in employees) e.Department = dept.Name;
    }
}
