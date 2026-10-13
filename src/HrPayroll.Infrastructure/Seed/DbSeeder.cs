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
                // الوردية الأولى: التكوين المعتمد رسميًا 07:00 - 15:00 مع سماح 30 دقيقة دخولًا وخروجًا.
                new Shift
                {
                    ShiftName = "الوردية الأولى", ShiftNumber = 1,
                    StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0),
                    StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30,
                    WorkingDays = "0,1,2,3,4,6", IsOvernight = false, OvertimeMultiplier = 1m, IsActive = true,
                    Notes = "الوردية الصباحية الافتراضية 07:00 - 15:00"
                },
                // الورديتان الثانية والثالثة: جدولهما غير محدد من الشركة، لذلك يبقيان قابلين للتعديل من شاشة الورديات.
                new Shift
                {
                    ShiftName = "الوردية الثانية", ShiftNumber = 2,
                    StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(23, 0, 0),
                    StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30,
                    WorkingDays = "0,1,2,3,4,6", IsOvernight = false, OvertimeMultiplier = 1m, IsActive = true,
                    Notes = "مواعيد مؤقتة — يجب على المسؤول ضبطها حسب سياسة الشركة"
                },
                new Shift
                {
                    ShiftName = "الوردية الثالثة (ليلية)", ShiftNumber = 3,
                    StartTime = new TimeSpan(23, 0, 0), EndTime = new TimeSpan(7, 0, 0),
                    StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30,
                    WorkingDays = "0,1,2,3,4,6", IsOvernight = true, OvertimeMultiplier = 1m, IsActive = true,
                    Notes = "مواعيد مؤقتة — يجب على المسؤول ضبطها حسب سياسة الشركة"
                }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("تم إنشاء الورديات الافتراضية");
        }

        var defaults = new Dictionary<string, (string Value, string Desc)>
        {
            // ===== سياسة الأجر =====
            [SettingKeys.WorkDaysPerMonth] = ("30", "عدد أيام الشهر المستخدم لحساب الأجر اليومي (الأجر اليومي = الراتب / هذا الرقم)"),
            [SettingKeys.WorkHoursPerDay] = ("8", "ساعات العمل اليومية (الأجر بالساعة = الأجر اليومي / هذا الرقم)"),
            [SettingKeys.DefaultOvertimeMultiplier] = ("1", "معامل الإضافي. سياسة الشركة = 1 (بدون أي مضاعفة): الإضافي يُدفع بسعر الساعة العادي"),
            [SettingKeys.LateDeductionEnabled] = ("true", "تفعيل خصم التأخير من الراتب"),
            [SettingKeys.AbsenceDeductionEnabled] = ("true", "تفعيل خصم أيام الغياب (سعر اليوم = الأجر اليومي)"),
            [SettingKeys.UnpaidLeaveDeductionEnabled] = ("true", "تفعيل خصم الإجازات بدون أجر"),
            [SettingKeys.OvertimeRoundingStepMinutes] = ("1", "خطوة تقريب الإضافي النهائي بالدقائق: 1 = بلا تقريب، ويمكن استخدام 5 أو 15 أو 30"),
            [SettingKeys.RoundOvertimeDown] = ("true", "تقريب الإضافي للأسفل عند استخدام خطوة تقريب أكبر من 1"),
            [SettingKeys.RequireOvertimeApproval] = ("true", "يتطلب اعتماد الإضافي يدويًا قبل إضافته للراتب"),
            [SettingKeys.AllowPayrollWithoutAttendance] = ("false", "السماح باحتساب كشف لفترة بلا بيانات حضور معالجة (غير مُستحسن)"),
            [SettingKeys.MonthlySalaryIncludesPaidRestDays] = ("true", "الراتب الشهري يشمل بالفعل أيام الراحة المدفوعة فلا تُضاف قيمتها مرة أخرى"),
            [SettingKeys.AnnualLeaveEntitlementDays] = ("21", "رصيد الإجازة السنوية الافتراضي باليوم للموظف الجديد"),

            // ===== الحضور =====
            [SettingKeys.WeeklyRestDays] = ("5", "أيام الراحة الأسبوعية (5=الجمعة، 6=السبت) مثال: 5,6"),
            [SettingKeys.EntryGraceMinutes] = ("30", "دقائق السماح الافتراضية عند الدخول (تُستخدم عند عدم ضبطها في الوردية)"),
            [SettingKeys.ExitGraceMinutes] = ("30", "دقائق السماح الافتراضية عند الخروج (تُستخدم عند عدم ضبطها في الوردية)"),
            [SettingKeys.MinimumOvertimeMinutes] = ("1", "أقل مدة إضافي معتبرة بالدقائق"),
            [SettingKeys.AutoProcessAfterImport] = ("false", "معالجة الحضور تلقائيًا بعد كل استيراد ملف بصمات"),

            // ===== عام =====
            [SettingKeys.CompanyTimeZone] = ("Egypt Standard Time", "المنطقة الزمنية المحلية للشركة"),
            [SettingKeys.CompanyName] = ("شركة الموارد البشرية", "اسم الشركة الظاهر في التقارير وكشوف الرواتب"),
            [SettingKeys.Currency] = ("EGP", "عملة الرواتب")
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
