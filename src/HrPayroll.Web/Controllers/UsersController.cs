using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuditService _audit;

    public UsersController(UserManager<ApplicationUser> userManager, AuditService audit)
    {
        _userManager = userManager; _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await _userManager.Users.ToListAsync(ct);
        var rows = new List<(ApplicationUser User, string Roles)>();
        foreach (var u in users)
            rows.Add((u, string.Join(", ", await _userManager.GetRolesAsync(u))));
        ViewBag.Roles = rows;
        return View(users);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.RoleList = new SelectList(AppRoles.All);
        return View("Form", new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.RoleList = new SelectList(AppRoles.All, vm.Role);
            return View("Form", vm);
        }

        var user = new ApplicationUser
        {
            UserName = vm.Email.Trim(),
            Email = vm.Email.Trim(),
            FullName = vm.FullName.Trim(),
            IsActive = vm.IsActive,
            EmailConfirmed = true
        };
        var result = await _userManager.CreateAsync(user, vm.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            ViewBag.RoleList = new SelectList(AppRoles.All, vm.Role);
            return View("Form", vm);
        }

        if (AppRoles.All.Contains(vm.Role)) await _userManager.AddToRoleAsync(user, vm.Role);
        _audit.Log("إضافة مستخدم", nameof(ApplicationUser), user.Id, null, $"{user.Email} - {vm.Role}", User.Identity?.Name);
        TempData["Success"] = "تم إنشاء المستخدم";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (user.Email == "admin@hr.local") { TempData["Error"] = "لا يمكن تعطيل حساب المدير الافتراضي"; return RedirectToAction(nameof(Index)); }
        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        _audit.Log(user.IsActive ? "تفعيل مستخدم" : "تعطيل مستخدم", nameof(ApplicationUser), user.Id, null, user.IsActive.ToString(), User.Identity?.Name);
        TempData["Success"] = "تم تحديث حالة المستخدم";
        return RedirectToAction(nameof(Index));
    }
}
