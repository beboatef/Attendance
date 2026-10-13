using System.Globalization;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

public static class SettingKeys
{
    // ===== سياسة الأجر =====
    public const string WorkDaysPerMonth = "Payroll.WorkDaysPerMonth";
    public const string WorkHoursPerDay = "Payroll.WorkHoursPerDay";
    public const string DefaultOvertimeMultiplier = "Payroll.DefaultOvertimeMultiplier";
    public const string LateDeductionEnabled = "Payroll.LateDeductionEnabled";
    public const string AbsenceDeductionEnabled = "Payroll.AbsenceDeductionEnabled";
    public const string UnpaidLeaveDeductionEnabled = "Payroll.UnpaidLeaveDeductionEnabled";
    public const string OvertimeRoundingStepMinutes = "Payroll.OvertimeRoundingStepMinutes";
    public const string RoundOvertimeDown = "Payroll.RoundOvertimeDown";
    public const string RequireOvertimeApproval = "Payroll.RequireOvertimeApproval";
    /// <summary>السماح باحتساب كشف لفترة بلا أي بيانات حضور معالجة (افتراضيًا: لا).</summary>
    public const string AllowPayrollWithoutAttendance = "Payroll.AllowPayrollWithoutAttendance";
    public const string MonthlySalaryIncludesPaidRestDays = "Payroll.MonthlySalaryIncludesPaidRestDays";
    public const string AnnualLeaveEntitlementDays = "Payroll.AnnualLeaveEntitlementDays";

    // ===== الحضور =====
    public const string WeeklyRestDays = "Attendance.WeeklyRestDays";
    public const string EntryGraceMinutes = "Attendance.EntryGraceMinutes";
    public const string ExitGraceMinutes = "Attendance.ExitGraceMinutes";
    public const string MinimumOvertimeMinutes = "Attendance.MinimumOvertimeMinutes";
    public const string AutoProcessAfterImport = "Attendance.AutoProcessAfterImport";

    // ===== عام =====
    public const string CompanyTimeZone = "General.CompanyTimeZone";
    public const string CompanyName = "General.CompanyName";
    public const string Currency = "General.Currency";
}

/// <summary>قراءة وكتابة إعدادات النظام وبناء سياسة الحساب منها.</summary>
public class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public async Task<List<AppSetting>> GetAllOrderedAsync(CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default)
    {
        var v = await GetAsync(key, ct);
        return v is null ? fallback : bool.TryParse(v, out var b) ? b : fallback;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        var v = await GetAsync(key, ct);
        return v is not null && int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : fallback;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default)
    {
        var v = await GetAsync(key, ct);
        return v is not null && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    /// <summary>سياسة الحضور والراتب الفعّالة من الإعدادات.</summary>
    public async Task<AttendancePolicy> GetPolicyAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        int GetInt(string k, int def) => all.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : def;
        decimal GetDec(string k, decimal def) => all.TryGetValue(k, out var v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : def;
        bool GetBool(string k, bool def) => all.TryGetValue(k, out var v) && bool.TryParse(v, out var r) ? r : def;

        return new AttendancePolicy
        {
            WorkDaysPerMonth = Math.Max(1, GetInt(SettingKeys.WorkDaysPerMonth, 30)),
            WorkHoursPerDay = Math.Max(1, GetInt(SettingKeys.WorkHoursPerDay, 8)),
            DefaultOvertimeMultiplier = GetDec(SettingKeys.DefaultOvertimeMultiplier, 1m),
            LateDeductionEnabled = GetBool(SettingKeys.LateDeductionEnabled, true),
            AbsenceDeductionEnabled = GetBool(SettingKeys.AbsenceDeductionEnabled, true),
            UnpaidLeaveDeductionEnabled = GetBool(SettingKeys.UnpaidLeaveDeductionEnabled, true),
            OvertimeRoundingStepMinutes = Math.Max(1, GetInt(SettingKeys.OvertimeRoundingStepMinutes, 1)),
            RoundOvertimeDown = GetBool(SettingKeys.RoundOvertimeDown, true),
            MonthlySalaryIncludesPaidRestDays = GetBool(SettingKeys.MonthlySalaryIncludesPaidRestDays, true),
            EntryGraceMinutes = Math.Max(0, GetInt(SettingKeys.EntryGraceMinutes, 30)),
            ExitGraceMinutes = Math.Max(0, GetInt(SettingKeys.ExitGraceMinutes, 30)),
            MinimumOvertimeMinutes = Math.Max(1, GetInt(SettingKeys.MinimumOvertimeMinutes, 1))
        };
    }

    /// <summary>أيام الراحة الأسبوعية المُعدّة (افتراضيًا الجمعة = 5).</summary>
    public async Task<HashSet<DayOfWeek>> GetWeeklyRestDaysAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(SettingKeys.WeeklyRestDays, ct);
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(raw)) { set.Add(DayOfWeek.Friday); return set; }
        foreach (var part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out var d) && d is >= 0 and <= 6) set.Add((DayOfWeek)d);
        return set;
    }

    public async Task SetAsync(string key, string value, string? description = null, string? user = null, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description, CreatedBy = user });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
            setting.UpdatedBy = user;
        }
        await _db.SaveChangesAsync(ct);
    }
}
