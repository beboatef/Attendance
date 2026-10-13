using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>إدارة بيانات المسميات الوظيفية المرجعية.</summary>
[Authorize(Policy = "ManageEmployees")]
public class JobTitlesController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public JobTitlesController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(string? search, int? departmentId, CancellationToken ct)
    {
        var q = _db.JobTitles.AsNoTracking().Include(j => j.Department).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(j => j.Name.Contains(search.Trim()));
        if (departmentId.HasValue) q = q.Where(j => j.DepartmentId == departmentId.Value);

        ViewBag.Search = search;
        ViewBag.DepartmentId = departmentId;
        ViewBag.Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        ViewBag.EmployeeCounts = await _db.Employees.AsNoTracking()
            .Where(e => e.JobTitleId != null)
            .GroupBy(e => e.JobTitleId!.Value)
            .Select(g => new { JobTitleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobTitleId, x => x.Count, ct);
        return View(await q.OrderBy(j => j.Name).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View("Form", new JobTitleFormViewModel
        {
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(JobTitleFormViewModel vm, CancellationToken ct)
    {
        if (await _db.JobTitles.AnyAsync(j => j.Name == vm.Name.Trim(), ct))
            ModelState.AddModelError(nameof(vm.Name), "يوجد مسمى وظيفي بنفس الاسم");
        if (!ModelState.IsValid)
        {
            vm.Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
            return View("Form", vm);
        }

        var job = new JobTitle
        {
            Name = vm.Name.Trim(),
            Code = vm.Code?.Trim(),
            DepartmentId = vm.DepartmentId,
            IsActive = vm.IsActive,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = this.CurrentUserName()
        };
        _db.JobTitles.Add(job);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("إضافة مسمى وظيفي", nameof(JobTitle), job.Id.ToString(), null, job.Name,
            this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم إضافة المسمى الوظيفي";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var job = await _db.JobTitles.FindAsync(new object?[] { id }, ct);
        if (job is null) return NotFound();
        return View("Form", new JobTitleFormViewModel
        {
            Id = job.Id, Name = job.Name, Code = job.Code, DepartmentId = job.DepartmentId,
            IsActive = job.IsActive, Notes = job.Notes,
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, JobTitleFormViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        if (await _db.JobTitles.AnyAsync(j => j.Name == vm.Name.Trim() && j.Id != id, ct))
            ModelState.AddModelError(nameof(vm.Name), "يوجد مسمى وظيفي بنفس الاسم");
        if (!ModelState.IsValid)
        {
            vm.Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
            return View("Form", vm);
        }

        var job = await _db.JobTitles.FindAsync(new object?[] { id }, ct);
        if (job is null) return NotFound();

        var old = $"{job.Name}|{job.DepartmentId}|{job.IsActive}";
        job.Name = vm.Name.Trim();
        job.Code = vm.Code?.Trim();
        job.DepartmentId = vm.DepartmentId;
        job.IsActive = vm.IsActive;
        job.Notes = vm.Notes;
        job.UpdatedAt = DateTime.UtcNow;
        job.UpdatedBy = this.CurrentUserName();

        var employees = await _db.Employees.Where(e => e.JobTitleId == job.Id).ToListAsync(ct);
        foreach (var e in employees) e.JobTitle = job.Name;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعديل مسمى وظيفي", nameof(JobTitle), job.Id.ToString(), old,
            $"{job.Name}|{job.DepartmentId}|{job.IsActive}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم تحديث المسمى الوظيفي";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var job = await _db.JobTitles.FindAsync(new object?[] { id }, ct);
        if (job is null) return NotFound();
        job.IsActive = !job.IsActive;
        job.UpdatedAt = DateTime.UtcNow;
        job.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync(job.IsActive ? "تفعيل مسمى وظيفي" : "تعطيل مسمى وظيفي", nameof(JobTitle), job.Id.ToString(),
            null, job.IsActive.ToString(), this.CurrentUserName(), this.CurrentUserId(), ct);
        TempData["Success"] = job.IsActive ? "تم التفعيل" : "تم التعطيل";
        return RedirectToAction(nameof(Index));
    }
}
