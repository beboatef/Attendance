using System.Globalization;
using Microsoft.AspNetCore.Localization;
using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Seed;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// قاعدة البيانات: SQL Server
// القيمة تُقرأ بالترتيب: appsettings.json ثم appsettings.Production.json
// ثم User Secrets (في بيئة Development) ثم متغيّر البيئة
// ConnectionStrings__DefaultConnection الذي يتقدّم على كل ما سبق.
const string UnsetPasswordPlaceholder = "__SET_PASSWORD__";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

// الهوية والأدوار
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;

        // سياسة محاولات الدخول الفاشلة: قفل مؤقت للحساب بعد 5 محاولات خاطئة
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// الصلاحيات
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.AdminOnly, p => p.RequireRole(AppRoles.Admin));
    options.AddPolicy(AppPolicies.ManageEmployees, p => p.RequireRole(AppRoles.Admin, AppRoles.Hr));
    options.AddPolicy(AppPolicies.ManageAttendance, p => p.RequireRole(AppRoles.Admin, AppRoles.Hr));
    options.AddPolicy(AppPolicies.ManagePayroll, p => p.RequireRole(AppRoles.Admin, AppRoles.Accountant));
    options.AddPolicy(AppPolicies.EditPayrollSheet, p => p.RequireRole(AppRoles.Admin, AppRoles.Accountant));
    options.AddPolicy(AppPolicies.ManageLoans, p => p.RequireRole(AppRoles.Admin, AppRoles.Accountant));
    options.AddPolicy(AppPolicies.ViewReports, p => p.RequireRole(AppRoles.All));
});

// خدمات التطبيق
builder.Services.AddScoped<AttendanceImportService>();
builder.Services.AddScoped<EmployeeImportService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<LeaveService>();
builder.Services.AddScoped<AttendanceCorrectionService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ShiftResolver>();
builder.Services.AddScoped<AttendanceProcessingService>();
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<PayrollSheetService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<DashboardService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// اللغة والثقافة الافتراضية: عربي مع تقويم ميلادي وأرقام لاتينية.
// لا تُستخدم ar-SA هنا لأنها تعتمد تقويم أم القرى (هجري): التواريخ كانت تُعرض
// وتُقرأ بصيغة هجرية (مثل 1447-07-16) فتنكسر حقول التاريخ وفلاتر التقارير.
var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-EG").Clone();
culture.DateTimeFormat.Calendar = new GregorianCalendar();
culture.NumberFormat.DigitSubstitution = DigitShapes.None;
culture.NumberFormat.NumberDecimalSeparator = ".";
culture.NumberFormat.CurrencyDecimalSeparator = ".";
culture.NumberFormat.NumberGroupSeparator = ",";
culture.NumberFormat.CurrencyGroupSeparator = ",";
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// تثبيت الثقافة على كل طلب حتى تكون قراءة/كتابة التواريخ والأرقام في النماذج
// والروابط موحّدة ولا تعتمد على حالة الخيط (Thread) الحالي.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = new[] { culture },
    SupportedUICultures = new[] { culture }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");
app.MapRazorPages();

// تطبيق الترحيلات وتهيئة البيانات عند الإقلاع
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    if (connectionString.Contains(UnsetPasswordPlaceholder, StringComparison.Ordinal))
    {
        // لا تُطبع أي بيانات اعتماد — تُذكر أسماء مفاتيح التهيئة فقط.
        logger.LogError(
            "لم يُضبط Connection String بعد: القيمة الفعّالة ما زالت تحتوي على القالب \"{Placeholder}\". " +
            "اضبط \"ConnectionStrings:DefaultConnection\" في appsettings.Production.json " +
            "(غير مُتتبَّع في Git) أو في User Secrets أو في متغيّر البيئة " +
            "ConnectionStrings__DefaultConnection. لن تُنفَّذ أي ترحيلات أو تهيئة بيانات.",
            UnsetPasswordPlaceholder);
    }
    else if (!builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
    {
        logger.LogInformation(
            "تم تخطّي الترحيلات وتهيئة البيانات عند الإقلاع (Database:ApplyMigrationsOnStartup=false). " +
            "لتفعيلها لاحقًا: dotnet ef database update -p src/HrPayroll.Infrastructure -s src/HrPayroll.Web");
    }
    else
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            // Migrate تُطبّق الترحيلات الناقصة فقط: لا تُعيد إنشاء قاعدة موجودة ولا تحذف أي جدول.
            db.Database.Migrate();

            // بيانات أول مسؤول تأتي من الإعدادات فقط (متغيّر بيئة أو User Secrets أو ملف غير مُتتبَّع)،
            // ولا تُوجود أي كلمة مرور افتراضية في الكود أو المستودع.
            var seedSection = builder.Configuration.GetSection("Seed");
            await DbSeeder.SeedAsync(
                db,
                services.GetRequiredService<RoleManager<IdentityRole>>(),
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                logger,
                new AdminSeedOptions
                {
                    IsDevelopment = app.Environment.IsDevelopment(),
                    Email = seedSection["AdminEmail"],
                    Password = seedSection["AdminPassword"],
                    FullName = seedSection["AdminFullName"]
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "تعذّر تهيئة قاعدة البيانات. تحقق من Connection String وشغّل الترحيلات.");
        }
    }
}

app.Run();

public static class AppPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ManageEmployees = "ManageEmployees";
    public const string ManageAttendance = "ManageAttendance";
    public const string ManagePayroll = "ManagePayroll";
    public const string EditPayrollSheet = "EditPayrollSheet";
    public const string ManageLoans = "ManageLoans";
    public const string ViewReports = "ViewReports";
}

// لجعل Program صالحًا للاختبارات المتكاملة
public partial class Program { }
