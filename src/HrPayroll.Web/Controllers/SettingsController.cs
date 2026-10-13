using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>إعدادات سياسة الحضور والرواتب والشركة.</summary>
[Authorize(Policy = "AdminOnly")]
public class SettingsController : Controller
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public SettingsController(SettingsService settings, AppDbContext db, AuditService audit)
    {
        _settings = settings;
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Settings = await _settings.GetAllOrderedAsync(ct);
        ViewBag.Policy = await _settings.GetPolicyAsync(ct);
        ViewBag.RestDays = await _settings.GetWeeklyRestDaysAsync(ct);
        ViewBag.AuditLogs = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.Timestamp).Take(50).ToListAsync(ct);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Dictionary<string, string> values, CancellationToken ct)
    {
        var allowed = new Dictionary<string, (Func<string, bool> Validate, string Message)>
        {
            [SettingKeys.WorkDaysPerMonth] = (v => int.TryParse(v, out var i) && i is >= 1 and <= 31, "عدد أيام الشهر يجب أن يكون بين 1 و 31"),
            [SettingKeys.WorkHoursPerDay] = (v => int.TryParse(v, out var i) && i is >= 1 and <= 24, "ساعات العمل يجب أن تكون بين 1 و 24"),
            [SettingKeys.DefaultOvertimeMultiplier] = (v => decimal.TryParse(v, out var d) && d == 1m, "سياسة الشركة تمنع مضاعفة الإضافي: القيمة يجب أن تكون 1"),
            [SettingKeys.OvertimeRoundingStepMinutes] = (v => int.TryParse(v, out var i) && i is >= 1 and <= 120, "خطوة التقريب يجب أن تكون بين 1 و 120 دقيقة"),
            [SettingKeys.EntryGraceMinutes] = (v => int.TryParse(v, out var i) && i is >= 0 and <= 240, "سماح الدخول يجب أن يكون بين 0 و 240 دقيقة"),
            [SettingKeys.ExitGraceMinutes] = (v => int.TryParse(v, out var i) && i is >= 0 and <= 240, "سماح الخروج يجب أن يكون بين 0 و 240 دقيقة"),
            [SettingKeys.MinimumOvertimeMinutes] = (v => int.TryParse(v, out var i) && i is >= 1 and <= 240, "أقل مدة إضافي يجب أن تكون بين 1 و 240 دقيقة"),
            [SettingKeys.AnnualLeaveEntitlementDays] = (v => int.TryParse(v, out var i) && i is >= 0 and <= 365, "رصيد الإجازة غير صالح"),
            [SettingKeys.WeeklyRestDays] = (v => !string.IsNullOrWhiteSpace(v), "أيام الراحة مطلوبة"),
            // مفاتيح النعم/المنع — يجب أن تكون كل عناصر الشاشة قابلة للحفظ فعليًا،
            // وإلا فقد يظن المستخدم أنه غيّر سياسة الحساب بينما لا يُحفظ أي تغيير.
            [SettingKeys.LateDeductionEnabled] = (IsBool, "قيمة خصم التأخير يجب أن تكون true أو false"),
            [SettingKeys.AbsenceDeductionEnabled] = (IsBool, "قيمة خصم الغياب يجب أن تكون true أو false"),
            [SettingKeys.UnpaidLeaveDeductionEnabled] = (IsBool, "قيمة خصم الإجازة بدون أجر يجب أن تكون true أو false"),
            [SettingKeys.RoundOvertimeDown] = (IsBool, "قيمة تقريب الإضافي يجب أن تكون true أو false"),
            [SettingKeys.RequireOvertimeApproval] = (IsBool, "قيمة اعتماد الإضافي يجب أن تكون true أو false"),
            [SettingKeys.AllowPayrollWithoutAttendance] = (IsBool, "قيمة احتساب كشف بلا بيانات حضور يجب أن تكون true أو false"),
            [SettingKeys.AutoProcessAfterImport] = (IsBool, "قيمة المعالجة التلقائية يجب أن تكون true أو false"),
            [SettingKeys.CompanyName] = (v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 200, "اسم الشركة مطلوب (200 حرف كحد أقصى)"),
            [SettingKeys.Currency] = (v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 10, "العملة مطلوبة (10 أحرف كحد أقصى)"),
            [SettingKeys.CompanyTimeZone] = (v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 100, "المنطقة الزمنية مطلوبة")
        };

        var errors = new List<string>();
        var updated = 0;
        var applied = new List<string>();

        foreach (var (key, value) in values)
        {
            if (!allowed.TryGetValue(key, out var rule)) continue;

            var normalized = key is SettingKeys.DefaultOvertimeMultiplier or SettingKeys.OvertimeRoundingStepMinutes
                or SettingKeys.EntryGraceMinutes or SettingKeys.ExitGraceMinutes
                or SettingKeys.MinimumOvertimeMinutes or SettingKeys.WorkDaysPerMonth
                or SettingKeys.WorkHoursPerDay or SettingKeys.AnnualLeaveEntitlementDays
                ? value.Trim()
                : value.Trim();

            if (!rule.Validate(normalized))
            {
                errors.Add(rule.Message);
                continue;
            }

            await _settings.SetAsync(key, normalized, null, this.CurrentUserName(), ct);
            updated++;
            applied.Add($"{key}={normalized}");
        }

        // يُسجَّل في سجل المراجعة ما تم حفظه فعلًا فقط (وليس القيم التي رُفضت أو تُجوهلت).
        await _audit.LogAndSaveAsync("تحديث إعدادات النظام", null, null, null,
            string.Join("; ", applied), this.CurrentUserName(), this.CurrentUserId(), ct);

        if (errors.Count > 0) TempData["Error"] = string.Join(" | ", errors.Distinct());
        if (updated > 0) TempData["Success"] = $"تم حفظ {updated} إعدادًا.";
        else if (errors.Count == 0) TempData["Error"] = "لم يتم إرسال أي إعداد صالح للحفظ.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>هل النص قيمة منطقية صريحة (تُستخدم لمفاتيح التشغيل/الإيقاف)؟</summary>
    private static bool IsBool(string value)
        => bool.TryParse(value?.Trim(), out _);
}
