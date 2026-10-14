using System.Security.Cryptography;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Infrastructure.Seed;

/// <summary>مصدر بيانات أول مسؤول: من الإعدادات (متغيّر بيئة/User Secrets) أو حساب تطوير مؤقت.</summary>
public sealed class AdminSeedOptions
{
    /// <summary>هل التطبيق يعمل في بيئة التطوير؟ لا يُنشأ أي حساب تجريبي خارجها.</summary>
    public bool IsDevelopment { get; init; }

    /// <summary>بريد أول مسؤول (Seed:AdminEmail).</summary>
    public string? Email { get; init; }

    /// <summary>كلمة مرور أول مسؤول (Seed:AdminPassword).</summary>
    public string? Password { get; init; }

    /// <summary>الاسم المعروض لأول مسؤول (Seed:AdminFullName).</summary>
    public string? FullName { get; init; }
}

/// <summary>تهيئة البيانات الأساسية: الأدوار، أول مسؤول، الورديات، الإعدادات.</summary>
public static class DbSeeder
{
    /// <summary>بريد حساب التطوير المحلي (بريد فقط — لا تُخزَّن أي كلمة مرور في الكود).</summary>
    public const string DevelopmentAdminEmail = "admin@hr.local";

    public static async Task SeedAsync(
        AppDbContext db,
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        AdminSeedOptions? adminSeed = null)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        await EnsureFirstAdminAsync(userManager, logger, adminSeed ?? new AdminSeedOptions());

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

    /// <summary>
    /// ينشئ أول مسؤول بطريقة آمنة:
    /// 1) إذا ضُبطت Seed:AdminEmail و Seed:AdminPassword (متغيّر بيئة أو User Secrets — وليس في المستودع)
    ///    يُنشأ/يُرقّى هذا الحساب.
    /// 2) إن لم تُضبط ووُجد مستخدمون بالفعل: لا يُنشأ شيء.
    /// 3) إن لم تُضبط وبيئة الإنتاج: لا يُنشأ أي حساب بكلمة مرور معروفة، وتُطبع تعليمات الضبط.
    /// 4) بيئة التطوير فقط: يُنشأ حساب تجريبي بكلمة مرور عشوائية تُعرض مرة واحدة في سجل بدء التشغيل.
    /// لا توجد أي كلمة مرور ثابتة في الكود أو المستودع.
    /// </summary>
    private static async Task EnsureFirstAdminAsync(
        UserManager<ApplicationUser> userManager, ILogger logger, AdminSeedOptions options)
    {
        var email = options.Email?.Trim();
        var password = options.Password;

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is null)
            {
                var created = await userManager.CreateAsync(new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    FullName = string.IsNullOrWhiteSpace(options.FullName) ? "مدير النظام" : options.FullName!,
                    IsActive = true
                }, password);

                if (!created.Succeeded)
                {
                    logger.LogWarning("تعذّر إنشاء أول مسؤول {Email}: {Errors}", email,
                        string.Join("; ", created.Errors.Select(e => e.Description)));
                    return;
                }
                existing = await userManager.FindByEmailAsync(email);
                logger.LogInformation("تم إنشاء أول مسؤول من الإعدادات: {Email}", email);
            }

            if (existing is not null && !await userManager.IsInRoleAsync(existing, AppRoles.Admin))
            {
                await userManager.AddToRoleAsync(existing, AppRoles.Admin);
                logger.LogInformation("تم منح دور المسؤول للحساب {Email}", email);
            }
            return;
        }

        if (await userManager.Users.AnyAsync())
        {
            logger.LogInformation("يوجد مستخدمون بالفعل في قاعدة البيانات؛ تم تخطي إنشاء أول مسؤول.");
            return;
        }

        if (!options.IsDevelopment)
        {
            logger.LogWarning(
                "لم يتم إنشاء أي مستخدم: لا توجد بيانات اعتماد لأول مسؤول. " +
                "اضبط \"Seed:AdminEmail\" و\"Seed:AdminPassword\" عبر متغيّر البيئة " +
                "Seed__AdminEmail / Seed__AdminPassword أو User Secrets ثم أعد تشغيل التطبيق. " +
                "لا يُنشأ أي حساب بكلمة مرور افتراضية في بيئة الإنتاج.");
            return;
        }

        // بيئة التطوير فقط: حساب تجريبي بكلمة مرور عشوائية تُعرض مرة واحدة في السجل.
        var devPassword = GenerateStrongPassword();
        var devUser = await userManager.FindByEmailAsync(DevelopmentAdminEmail);
        if (devUser is not null)
        {
            logger.LogInformation("حساب التطوير {Email} موجود بالفعل.", DevelopmentAdminEmail);
            return;
        }

        var devResult = await userManager.CreateAsync(new ApplicationUser
        {
            UserName = DevelopmentAdminEmail,
            Email = DevelopmentAdminEmail,
            EmailConfirmed = true,
            FullName = "مدير النظام (تطوير)",
            IsActive = true
        }, devPassword);

        if (!devResult.Succeeded)
        {
            logger.LogWarning("تعذّر إنشاء حساب التطوير: {Errors}",
                string.Join("; ", devResult.Errors.Select(e => e.Description)));
            return;
        }

        var devAdmin = await userManager.FindByEmailAsync(DevelopmentAdminEmail);
        if (devAdmin is not null) await userManager.AddToRoleAsync(devAdmin, AppRoles.Admin);

        logger.LogWarning(
            "[بيئة التطوير فقط] تم إنشاء حساب تجريبي {Email} بكلمة مرور عشوائية: {Password} — " +
            "احفظها الآن؛ لن تُعرض مرة أخرى. غيّر كلمة المرور أو اضبط Seed:AdminPassword في إعداداتك المحلية.",
            DevelopmentAdminEmail, devPassword);
    }

    /// <summary>كلمة مرور عشوائية قوية تُلبّي سياسة كلمة المرور (حروف كبيرة/صغيرة وأرقام ورمز).</summary>
    private static string GenerateStrongPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*?=";
        const string all = upper + lower + digits + symbols;

        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(symbols) };
        while (chars.Count < 16) chars.Add(Pick(all));

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
