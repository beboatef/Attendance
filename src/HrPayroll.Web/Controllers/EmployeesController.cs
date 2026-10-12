using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManageEmployees")]
public class EmployeesController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public EmployeesController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(string? search, string? department, bool? isActive, int page = 1, CancellationToken ct = default)
    {
        const int pageSize = 20;
        var query = _db.Employees.Include(e => e.Shift).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(e => e.FullName.Contains(s) || e.EmployeeCode.Contains(s)
                                     || (e.DeviceEmployeeCode != null && e.DeviceEmployeeCode.Contains(s)));
        }
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(e => e.Department == department);
        if (isActive.HasValue) query = query.Where(e => e.IsActive == isActive.Value);

        var total = await query.CountAsync(ct);
        var list = await query.OrderBy(e => e.EmployeeCode)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return View(new EmployeeIndexViewModel
        {
            Employees = list,
            Search = search,
            DepartmentFilter = department,
            IsActive = isActive,
            TotalCount = total,
            Page = Math.Max(1, page),
            PageSize = pageSize,
            Departments = await _db.Employees.Where(e => e.Department != null)
                .Select(e => e.Department!).Distinct().OrderBy(d => d).ToListAsync(ct)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new EmployeeFormViewModel { Shifts = await _db.Shifts.Where(s => s.IsActive).ToListAsync(ct) };
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeeFormViewModel vm, CancellationToken ct)
    {
        if (await _db.Employees.AnyAsync(e => e.EmployeeCode == vm.EmployeeCode, ct))
            ModelState.AddModelError(nameof(vm.EmployeeCode), "كود الموظف مستخدم مسبقًا");

        if (!ModelState.IsValid)
        {
            vm.Shifts = await _db.Shifts.Where(s => s.IsActive).ToListAsync(ct);
            return View("Form", vm);
        }

        var emp = new Employee
        {
            EmployeeCode = vm.EmployeeCode.Trim(),
            DeviceEmployeeCode = string.IsNullOrWhiteSpace(vm.DeviceEmployeeCode) ? vm.EmployeeCode.Trim() : vm.DeviceEmployeeCode!.Trim(),
            FullName = vm.FullName.Trim(),
            JobTitle = vm.JobTitle,
            Department = vm.Department,
            ShiftId = vm.ShiftId,
            BasicSalary = vm.BasicSalary,
            HireDate = vm.HireDate,
            IsActive = vm.IsActive,
            Email = vm.Email,
            Phone = vm.Phone,
            NationalId = vm.NationalId,
            Notes = vm.Notes,
            CreatedBy = User.Identity?.Name
        };
        _db.Employees.Add(emp);
        await _db.SaveChangesAsync(ct);
        _audit.Log("إضافة موظف", nameof(Employee), emp.Id.ToString(), null, emp.FullName, User.Identity?.Name, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "تم إضافة الموظف بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var emp = await _db.Employees.FindAsync(new object?[] { id }, ct);
        if (emp is null) return NotFound();
        var vm = EmployeeFormViewModel.FromEntity(emp);
        vm.Shifts = await _db.Shifts.Where(s => s.IsActive).ToListAsync(ct);
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EmployeeFormViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        if (await _db.Employees.AnyAsync(e => e.EmployeeCode == vm.EmployeeCode && e.Id != id, ct))
            ModelState.AddModelError(nameof(vm.EmployeeCode), "كود الموظف مستخدم مسبقًا");

        if (!ModelState.IsValid)
        {
            vm.Shifts = await _db.Shifts.Where(s => s.IsActive).ToListAsync(ct);
            return View("Form", vm);
        }

        var emp = await _db.Employees.FindAsync(new object?[] { id }, ct);
        if (emp is null) return NotFound();

        var old = $"{emp.EmployeeCode}|{emp.FullName}|{emp.BasicSalary}";
        emp.EmployeeCode = vm.EmployeeCode.Trim();
        emp.DeviceEmployeeCode = vm.DeviceEmployeeCode;
        emp.FullName = vm.FullName.Trim();
        emp.JobTitle = vm.JobTitle;
        emp.Department = vm.Department;
        emp.ShiftId = vm.ShiftId;
        emp.BasicSalary = vm.BasicSalary;
        emp.HireDate = vm.HireDate;
        emp.IsActive = vm.IsActive;
        emp.Email = vm.Email;
        emp.Phone = vm.Phone;
        emp.NationalId = vm.NationalId;
        emp.Notes = vm.Notes;
        emp.UpdatedAt = DateTime.UtcNow;
        emp.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        _audit.Log("تعديل موظف", nameof(Employee), emp.Id.ToString(), old,
            $"{emp.EmployeeCode}|{emp.FullName}|{emp.BasicSalary}", User.Identity?.Name);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "تم تحديث بيانات الموظف";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var emp = await _db.Employees.FindAsync(new object?[] { id }, ct);
        if (emp is null) return NotFound();
        emp.IsActive = !emp.IsActive;
        emp.UpdatedAt = DateTime.UtcNow;
        emp.UpdatedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        _audit.Log(emp.IsActive ? "تفعيل موظف" : "تعطيل موظف", nameof(Employee), emp.Id.ToString(), null, emp.IsActive.ToString(), User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = emp.IsActive ? "تم تفعيل الموظف" : "تم تعطيل الموظف";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var emp = await _db.Employees.Include(e => e.Shift).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (emp is null) return NotFound();
        return View(emp);
    }
}
