using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;

    public SettingsController(AppDbContext db, SettingsService settings, AuditService audit)
    {
        _db = db; _settings = settings; _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _db.AppSettings.AsNoTracking().OrderBy(s => s.Group).ThenBy(s => s.Key).ToListAsync(ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Dictionary<string, string> values, CancellationToken ct)
    {
        foreach (var (key, value) in values)
        {
            var old = await _settings.GetAsync(key, ct);
            if (old == value) continue;
            await _settings.SetAsync(key, value, null, User.Identity?.Name, ct);
            _audit.Log("تعديل إعداد", nameof(Domain.Entities.AppSetting), key, old, value, User.Identity?.Name);
        }
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم حفظ الإعدادات";
        return RedirectToAction(nameof(Index));
    }
}
