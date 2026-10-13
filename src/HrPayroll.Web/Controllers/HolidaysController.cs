using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>إدارة العطلات الرسمية وأيام الراحة الأسبوعية.</summary>
[Authorize(Policy = "ManageAttendance")]
public class HolidaysController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly SettingsService _settings;

    public HolidaysController(AppDbContext db, AuditService audit, SettingsService settings)
    {
        _db = db;
        _audit = audit;
        _settings = settings;
    }

    public async Task<IActionResult> Index(int? year, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        ViewBag.Year = y;
        var holidays = await _db.PublicHolidays.AsNoTracking()
            .Where(h => h.HolidayDate.Year == y)
            .OrderBy(h => h.HolidayDate)
            .ToListAsync(ct);
        ViewBag.RestDays = await _settings.GetAsync(SettingKeys.WeeklyRestDays, ct) ?? "5";
        return View(holidays);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRestDays(string? restDays, CancellationToken ct)
    {
        var normalized = string.Join(",", (restDays ?? string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var d) && d is >= 0 and <= 6 ? d.ToString() : null)
            .Where(x => x is not null)
            .Distinct()
            .OrderBy(x => x));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            TempData["Error"] = "يجب اختيار يوم راحة واحد على الأقل";
            return RedirectToAction(nameof(Index));
        }

        await _settings.SetAsync(SettingKeys.WeeklyRestDays, normalized,
            "أيام الراحة الأسبوعية (5=الجمعة، 6=السبت)", this.CurrentUserName(), ct);
        TempData["Success"] = "تم تحديث أيام الراحة الأسبوعية";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new HolidayFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HolidayFormViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Form", vm);

        var holiday = new PublicHoliday
        {
            Name = vm.Name.Trim(),
            HolidayDate = vm.HolidayDate.Date,
            IsPaid = vm.IsPaid,
            AllowWorkWithExtraPay = vm.AllowWorkWithExtraPay,
            IsActive = vm.IsActive,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = this.CurrentUserName()
        };
        _db.PublicHolidays.Add(holiday);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("إضافة عطلة رسمية", nameof(PublicHoliday), holiday.Id.ToString(), null,
            $"{holiday.Name} {holiday.HolidayDate:yyyy-MM-dd}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم إضافة العطلة الرسمية";
        return RedirectToAction(nameof(Index), new { year = holiday.HolidayDate.Year });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var h = await _db.PublicHolidays.FindAsync(new object?[] { id }, ct);
        if (h is null) return NotFound();
        return View("Form", new HolidayFormViewModel
        {
            Id = h.Id, Name = h.Name, HolidayDate = h.HolidayDate, IsPaid = h.IsPaid,
            AllowWorkWithExtraPay = h.AllowWorkWithExtraPay, IsActive = h.IsActive, Notes = h.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, HolidayFormViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) return View("Form", vm);

        var h = await _db.PublicHolidays.FindAsync(new object?[] { id }, ct);
        if (h is null) return NotFound();

        var old = $"{h.Name}|{h.HolidayDate:yyyy-MM-dd}|{h.IsPaid}";
        h.Name = vm.Name.Trim();
        h.HolidayDate = vm.HolidayDate.Date;
        h.IsPaid = vm.IsPaid;
        h.AllowWorkWithExtraPay = vm.AllowWorkWithExtraPay;
        h.IsActive = vm.IsActive;
        h.Notes = vm.Notes;
        h.UpdatedAt = DateTime.UtcNow;
        h.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعديل عطلة رسمية", nameof(PublicHoliday), h.Id.ToString(), old,
            $"{h.Name}|{h.HolidayDate:yyyy-MM-dd}|{h.IsPaid}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم تحديث العطلة";
        return RedirectToAction(nameof(Index), new { year = h.HolidayDate.Year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var h = await _db.PublicHolidays.FindAsync(new object?[] { id }, ct);
        if (h is null) return NotFound();
        h.IsActive = false;
        h.UpdatedAt = DateTime.UtcNow;
        h.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعطيل عطلة رسمية", nameof(PublicHoliday), h.Id.ToString(), null, "IsActive=false",
            this.CurrentUserName(), this.CurrentUserId(), ct);
        TempData["Success"] = "تم تعطيل العطلة الرسمية (لن تُحتسب في المعالجة)";
        return RedirectToAction(nameof(Index), new { year = h.HolidayDate.Year });
    }
}
