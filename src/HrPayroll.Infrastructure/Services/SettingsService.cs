using System.Globalization;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Models;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

public static class SettingKeys
{
    public const string WorkDaysPerMonth = "Payroll.WorkDaysPerMonth";
    public const string WorkHoursPerDay = "Payroll.WorkHoursPerDay";
    public const string DefaultOvertimeMultiplier = "Payroll.DefaultOvertimeMultiplier";
    public const string LateDeductionEnabled = "Payroll.LateDeductionEnabled";
    public const string AbsenceDeductionEnabled = "Payroll.AbsenceDeductionEnabled";
    public const string CompanyTimeZone = "General.CompanyTimeZone";
    public const string WeeklyRestDays = "Attendance.WeeklyRestDays";
    public const string RequireOvertimeApproval = "Payroll.RequireOvertimeApproval";
    public const string Currency = "General.Currency";
}

/// <summary>قراءة وكتابة إعدادات النظام وبناء سياسة الحساب منها.</summary>
public class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    public async Task<AttendancePolicy> GetPolicyAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        int GetInt(string k, int def) => all.TryGetValue(k, out var v) && int.TryParse(v, out var r) ? r : def;
        decimal GetDec(string k, decimal def) => all.TryGetValue(k, out var v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : def;
        bool GetBool(string k, bool def) => all.TryGetValue(k, out var v) && bool.TryParse(v, out var r) ? r : def;

        return new AttendancePolicy
        {
            WorkDaysPerMonth = GetInt(SettingKeys.WorkDaysPerMonth, 30),
            WorkHoursPerDay = GetInt(SettingKeys.WorkHoursPerDay, 8),
            DefaultOvertimeMultiplier = GetDec(SettingKeys.DefaultOvertimeMultiplier, 1m),
            LateDeductionEnabled = GetBool(SettingKeys.LateDeductionEnabled, true),
            AbsenceDeductionEnabled = GetBool(SettingKeys.AbsenceDeductionEnabled, false)
        };
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
