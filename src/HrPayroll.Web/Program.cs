using System.Globalization;
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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

// الهوية والأدوار
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
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
    options.AddPolicy(AppPolicies.ManageLoans, p => p.RequireRole(AppRoles.Admin, AppRoles.Accountant));
    options.AddPolicy(AppPolicies.ViewReports, p => p.RequireRole(AppRoles.All));
});

// خدمات التطبيق
builder.Services.AddScoped<AttendanceImportService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ShiftResolver>();
builder.Services.AddScoped<AttendanceProcessingService>();
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<DashboardService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// اللغة والثقافة الافتراضية (عربي)
var culture = new CultureInfo("ar-SA");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

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
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await DbSeeder.SeedAsync(
            db,
            services.GetRequiredService<RoleManager<IdentityRole>>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "تعذّر تهيئة قاعدة البيانات. تحقق من Connection String وشغّل الترحيلات.");
    }
}

app.Run();

public static class AppPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ManageEmployees = "ManageEmployees";
    public const string ManageAttendance = "ManageAttendance";
    public const string ManagePayroll = "ManagePayroll";
    public const string ManageLoans = "ManageLoans";
    public const string ViewReports = "ViewReports";
}

// لجعل Program صالحًا للاختبارات المتكاملة
public partial class Program { }
