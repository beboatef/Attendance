using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Infrastructure.Seed;

/// <summary>تهيئة البيانات الأساسية: الأدوار، المستخدم المدير، الورديات، الإعدادات.</summary>
public static class DbSeeder
{
    public const string DefaultAdminEmail = "admin@hr.local";
    public const string DefaultAdminPassword = "Admin@12345";

    public static async Task SeedAsync(
        AppDbContext db,
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger logger)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        if (await userManager.FindByEmailAsync(DefaultAdminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = DefaultAdminEmail,
                Email = DefaultAdminEmail,
                EmailConfirmed = true,
                FullName = "مدير النظام",
                IsActive = true
            };
            var res = await userManager.CreateAsync(admin, DefaultAdminPassword);
            if (res.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, AppRoles.Admin);
                logger.LogInformation("تم إنشاء المستخدم الافتراضي {Email}", DefaultAdminEmail);
            }
            else
            {
                logger.LogWarning("تعذّر إنشاء المستخدم الافتراضي: {Errors}",
                    string.Join("; ", res.Errors.Select(e => e.Description)));
            }
        }

        if (!await db.Shifts.AnyAsync())
        {
            db.Shifts.AddRange(
                new Shift { ShiftName = "الوردية الأولى", StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0), GraceMinutes = 30, IsOvernight = false, OvertimeMultiplier = 1m, IsActive = true },
                new Shift { ShiftName = "الوردية الثانية", StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(23, 0, 0), GraceMinutes = 0, IsOvernight = false, OvertimeMultiplier = 1m, IsActive = true },
                new Shift { ShiftName = "الوردية الثالثة (ليلية)", StartTime = new TimeSpan(23, 0, 0), EndTime = new TimeSpan(7, 0, 0), GraceMinutes = 0, IsOvernight = true, OvertimeMultiplier = 1m, IsActive = true }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("تم إنشاء الورديات الافتراضية");
        }

        var defaults = new Dictionary<string, (string Value, string Desc)>
        {
            [SettingKeys.WorkDaysPerMonth] = ("30", "عدد أيام العمل في الشهر المستخدم لحساب قيمة الساعة"),
            [SettingKeys.WorkHoursPerDay] = ("8", "عدد ساعات العمل اليومية"),
            [SettingKeys.DefaultOvertimeMultiplier] = ("1", "معامل الإضافي الافتراضي"),
            [SettingKeys.LateDeductionEnabled] = ("true", "تفعيل خصم التأخير من الراتب"),
            [SettingKeys.AbsenceDeductionEnabled] = ("false", "تفعيل خصم الغياب (لا يُفعّل إلا بسياسة معتمدة)"),
            [SettingKeys.RequireOvertimeApproval] = ("true", "يتطلب اعتماد الإضافي يدويًا قبل إضافته للراتب"),
            [SettingKeys.CompanyTimeZone] = ("Arab Standard Time", "المنطقة الزمنية المحلية للشركة"),
            [SettingKeys.WeeklyRestDays] = ("5", "أيام الراحة الأسبوعية (5=الجمعة، 6=السبت) مثال: 5,6"),
            [SettingKeys.Currency] = ("SAR", "عملة الرواتب")
        };

        var existingKeys = await db.AppSettings.Select(s => s.Key).ToListAsync();
        foreach (var (key, val) in defaults)
        {
            if (existingKeys.Contains(key)) continue;
            db.AppSettings.Add(new AppSetting { Key = key, Value = val.Value, Description = val.Desc, CreatedBy = "system" });
        }
        await db.SaveChangesAsync();
    }
}
