using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Seed;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HrPayroll.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _env = env;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        PrepareLoginViewData(returnUrl);
        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        PrepareLoginViewData(returnUrl);
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة أو الحساب غير مفعّل");
            return View(model);
        }

        // lockoutOnFailure: true => تُطبَّق سياسة المحاولات الفاشلة على الحساب
        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            _logger.LogWarning("محاولة دخول لحساب مقفل مؤقتًا: {Email}", model.Email);
            ModelState.AddModelError(string.Empty,
                "تم قفل الحساب مؤقتًا لتكرار محاولات الدخول الفاشلة. حاول مرة أخرى بعد 15 دقيقة أو تواصل مع مسؤول النظام.");
            return View(model);
        }
        if (result.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "هذا الحساب غير مفعّل. تواصل مع مسؤول النظام.");
            return View(model);
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة");
            return View(model);
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    /// <summary>تلميح الحساب التجريبي يُعرض في بيئة التطوير فقط — لا تظهر أي بيانات دخول في الإنتاج.</summary>
    private void PrepareLoginViewData(string? returnUrl)
    {
        ViewData["ReturnUrl"] = returnUrl;
        // تلميح الحساب التجريبي في بيئة التطوير فقط — لا شيء من هذا يظهر في الإنتاج
        ViewData["ShowDemoHint"] = _env.IsDevelopment();
        ViewData["DemoAdminEmail"] = _env.IsDevelopment() ? DbSeeder.DevelopmentAdminEmail : null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
