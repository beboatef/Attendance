using ClosedXML.Excel;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Seed;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>
/// تقرير الأقسام حسب المسمى الوظيفي، تقرير الإجازات والأرصدة،
/// استيراد ملفي الدخول والخروج بترتيب صفوف مختلف، وأمان إنشاء أول مسؤول.
/// </summary>
public class DepartmentAndLeaveReportTests
{
    private static readonly DateTime Day = TestDb.Monday;

    // ===================== أدوات =====================

    private static Employee Emp(int id, string code, string name, string dept, string job, decimal salary = 6000m)
        => new()
        {
            Id = id, EmployeeCode = code, DeviceEmployeeCode = code, FullName = name,
            Department = dept, JobTitle = job, BasicSalary = salary,
            AnnualLeaveEntitlement = 21m, AnnualLeaveCarriedOver = 0m, AnnualLeaveBalance = 21m,
            HireDate = new DateTime(2022, 1, 1), EmploymentStatus = EmploymentStatus.Active,
            IsActive = true, StandardWorkHours = 8m
        };

    private static DailyAttendance Present(int employeeId, DateTime day, int? lateMinutes = null)
        => new()
        {
            EmployeeId = employeeId, WorkDate = day, ShiftId = 1,
            ShiftStart = day.AddHours(7), ShiftEnd = day.AddHours(15),
            CheckInTime = day.AddHours(7).AddMinutes(lateMinutes ?? 0), CheckOutTime = day.AddHours(15),
            LateMinutes = lateMinutes ?? 0,
            Status = lateMinutes is > 0 ? AttendanceStatus.Late : AttendanceStatus.Present,
            PunchReviewStatus = ClassificationStatus.Classified
        };

    private static DailyAttendance Absent(int employeeId, DateTime day)
        => new()
        {
            EmployeeId = employeeId, WorkDate = day, ShiftId = 1,
            ShiftStart = day.AddHours(7), ShiftEnd = day.AddHours(15),
            Status = AttendanceStatus.Absent, PunchReviewStatus = ClassificationStatus.Classified
        };

    private static byte[] AttendanceFile(string attType, params (string code, string name, string time)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "No"; ws.Cell(1, 2).Value = "DeviceId"; ws.Cell(1, 3).Value = "ID";
        ws.Cell(1, 4).Value = "Name"; ws.Cell(1, 5).Value = "AttType"; ws.Cell(1, 6).Value = "Att.Time";
        var r = 2;
        foreach (var (code, name, time) in rows)
        {
            ws.Cell(r, 1).Value = r - 1; ws.Cell(r, 2).Value = "DEV-01"; ws.Cell(r, 3).Value = code;
            ws.Cell(r, 4).Value = name; ws.Cell(r, 5).Value = attType; ws.Cell(r, 6).Value = time; r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ===================== تقرير الأقسام والمسميات الوظيفية =====================

    [Fact]
    public async Task Department_Report_Groups_Real_Employees_Under_Their_Job_Titles()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());

        // قسم اللف: 2 مساعد + 4 صنايعي + 1 مشرف = 7 حاضرين (مثال المواصفات بالأرقام الفعلية من البيانات)
        ctx.Employees.AddRange(
            Emp(1, "2001", "مساعد أول", "اللف", "مساعد"),
            Emp(2, "2002", "مساعد ثاني", "اللف", "مساعد"),
            Emp(3, "2003", "صنايعي أول", "اللف", "صنايعي"),
            Emp(4, "2004", "صنايعي ثاني", "اللف", "صنايعي"),
            Emp(5, "2005", "صنايعي ثالث", "اللف", "صنايعي"),
            Emp(6, "2006", "صنايعي رابع", "اللف", "صنايعي"),
            Emp(7, "2007", "مشرف اللف", "اللف", "مشرف"),
            // موظف غائب في قسم آخر لاختبار عدّاد الغياب
            Emp(8, "3001", "محاسب", "المالية", "محاسب"));

        ctx.DailyAttendances.AddRange(
            Present(1, Day), Present(2, Day),
            Present(3, Day), Present(4, Day), Present(5, Day, 15), Present(6, Day),
            Present(7, Day),
            Absent(8, Day));
        await ctx.SaveChangesAsync();

        var reports = new ReportService(ctx, new SettingsService(ctx));
        var report = await reports.GetDepartmentJobTitleReportAsync(new AttendanceReportFilter { From = Day, To = Day });

        Assert.Equal(2, report.Count);

        var winding = report.Single(d => d.Department == "اللف");
        Assert.Equal(7, winding.TotalEmployees);
        Assert.Equal(7, winding.PresentEmployees);
        Assert.Equal(0, winding.AbsentEmployees);
        Assert.Equal(1, winding.LateEmployees);
        Assert.Equal(3, winding.JobTitles.Count);

        var assistants = winding.JobTitles.Single(j => j.JobTitle == "مساعد");
        Assert.Equal(2, assistants.EmployeeCount);
        Assert.Equal(2, assistants.PresentEmployees);
        Assert.Equal(new[] { "2001", "2002" }, assistants.Employees.Select(e => e.EmployeeCode).ToArray());

        var workers = winding.JobTitles.Single(j => j.JobTitle == "صنايعي");
        Assert.Equal(4, workers.EmployeeCount);
        Assert.Equal(new[] { "2003", "2004", "2005", "2006" }, workers.Employees.Select(e => e.EmployeeCode).ToArray());
        Assert.Equal(15, workers.Employees.Single(e => e.EmployeeCode == "2005").LateMinutes);

        var supervisor = winding.JobTitles.Single(j => j.JobTitle == "مشرف");
        Assert.Equal(1, supervisor.EmployeeCount);

        var finance = report.Single(d => d.Department == "المالية");
        Assert.Equal(1, finance.TotalEmployees);
        Assert.Equal(0, finance.PresentEmployees);
        Assert.Equal(1, finance.AbsentEmployees);

        // مجموع أعداد الموظفين في المسميات يساوي عدد موظفي القسم فعلًا
        Assert.Equal(winding.TotalEmployees, winding.JobTitles.Sum(j => j.EmployeeCount));
    }

    [Fact]
    public async Task Department_Report_Respects_JobTitle_And_Department_Filters()
    {
        var (db, ctx) = await SeedTwoDepartmentsAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));

        var onlyWorkers = await reports.GetDepartmentJobTitleReportAsync(
            new AttendanceReportFilter { From = Day, To = Day }, "صنايعي");
        Assert.Single(onlyWorkers);
        Assert.All(onlyWorkers[0].JobTitles, j => Assert.Equal("صنايعي", j.JobTitle));

        var financeOnly = await reports.GetDepartmentJobTitleReportAsync(
            new AttendanceReportFilter { From = Day, To = Day, Department = "المالية" });
        Assert.Single(financeOnly);
        Assert.Equal("المالية", financeOnly[0].Department);
    }

    private static async Task<(TestDb Db, AppDbContext Ctx)> SeedTwoDepartmentsAsync()
    {
        var db = new TestDb();
        var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.AddRange(
            Emp(1, "2001", "صنايعي أ", "اللف", "صنايعي"),
            Emp(2, "2002", "صنايعي ب", "اللف", "صنايعي"),
            Emp(3, "2003", "مساعد أ", "اللف", "مساعد"),
            Emp(4, "3001", "محاسب أ", "المالية", "محاسب"));
        ctx.DailyAttendances.AddRange(Present(1, Day), Present(2, Day), Present(3, Day), Present(4, Day));
        await ctx.SaveChangesAsync();
        return (db, ctx);
    }

    [Fact]
    public async Task Excel_Department_Report_Lists_Employee_Names_Under_Each_Job_Title()
    {
        var (db, ctx) = await SeedTwoDepartmentsAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var bytes = await excel.ExportDepartmentJobTitleAsync(
            new AttendanceReportFilter { From = Day, To = Day });
        using var wb = new XLWorkbook(new MemoryStream(bytes));

        var summary = wb.Worksheet("Departments");
        var employees = wb.Worksheet("Employees");

        // ملخص: سطر لكل قسم + مسمى + سطر إجمالي لكل قسم
        Assert.Equal("كود الموظف", employees.Cell(5, 3).GetString());
        Assert.Equal("الاسم", employees.Cell(5, 4).GetString());

        var codes = Enumerable.Range(6, employees.LastRowUsed()!.RowNumber() - 5)
            .Select(r => employees.Cell(r, 3).GetString())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
        Assert.Equal(new[] { "2001", "2002", "2003", "3001" }, codes.OrderBy(c => c).ToArray());

        var rowFor2003 = Enumerable.Range(6, employees.LastRowUsed()!.RowNumber() - 5)
            .Single(r => employees.Cell(r, 3).GetString() == "2003");
        Assert.Equal("اللف", employees.Cell(rowFor2003, 1).GetString());
        Assert.Equal("مساعد", employees.Cell(rowFor2003, 2).GetString());
        Assert.Equal("مساعد أ", employees.Cell(rowFor2003, 4).GetString());

        Assert.Contains("مساعد", Enumerable.Range(6, summary.LastRowUsed()!.RowNumber() - 5)
            .Select(r => summary.Cell(r, 2).GetString()));
        Assert.Contains(Enumerable.Range(6, summary.LastRowUsed()!.RowNumber() - 5)
            .Select(r => summary.Cell(r, 1).GetString()), v => v.StartsWith("إجمالي "));
    }

    // ===================== تقرير الإجازات والأرصدة =====================

    [Fact]
    public async Task Leaves_Report_And_Balances_Match_Stored_Leave_Data()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.AddRange(
            Emp(1, "1001", "مينا فؤاد", "المالية", "محاسب"),
            Emp(2, "1002", "سارة علي", "الموارد البشرية", "أخصائي"));
        await ctx.SaveChangesAsync();

        var employee = await ctx.Employees.SingleAsync(e => e.Id == 1);
        employee.AnnualLeaveEntitlement = 21m;
        employee.AnnualLeaveCarriedOver = 3m;
        employee.AnnualLeaveBalance = 19m;   // استُخدم 5 أيام من إجمالي 24

        ctx.LeaveRequests.AddRange(
            new LeaveRequest
            {
                EmployeeId = 1, StartDate = Day, EndDate = Day.AddDays(4), LeaveType = LeaveType.Annual,
                Status = ApprovalStatus.Approved, DaysCount = 5, WorkingDays = 5m, IsPaid = true,
                ApprovedBy = "مدير", ApprovedAt = Day.AddDays(-1), Reason = "إجازة سنوية"
            },
            new LeaveRequest
            {
                EmployeeId = 2, StartDate = Day, EndDate = Day, LeaveType = LeaveType.Unpaid,
                Status = ApprovalStatus.Pending, DaysCount = 1, WorkingDays = 1m, IsPaid = false
            });
        await ctx.SaveChangesAsync();

        var reports = new ReportService(ctx, new SettingsService(ctx));
        var filter = new AttendanceReportFilter { From = Day, To = Day.AddDays(4) };

        var leaves = await reports.GetLeavesAsync(filter);
        Assert.Equal(2, leaves.Count);
        var approved = leaves.Single(l => l.EmployeeCode == "1001");
        Assert.Equal(LeaveType.Annual, approved.LeaveType);
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(5m, approved.WorkingDays);
        Assert.Equal(24m, approved.Total);
        Assert.Equal(5m, approved.Used);
        Assert.Equal(19m, approved.Balance);

        var pendingOnly = await reports.GetLeavesAsync(filter, status: ApprovalStatus.Pending);
        Assert.Single(pendingOnly);
        Assert.Equal("1002", pendingOnly[0].EmployeeCode);

        var balances = await reports.GetLeaveBalancesAsync(new AttendanceReportFilter { From = Day, To = Day });
        Assert.Equal(2, balances.Count);
        var b1 = balances.Single(b => b.EmployeeCode == "1001");
        Assert.Equal(21m, b1.Entitlement);
        Assert.Equal(3m, b1.CarriedOver);
        Assert.Equal(24m, b1.Total);
        Assert.Equal(5m, b1.Used);
        Assert.Equal(19m, b1.Balance);

        // الفلترة بالقسم لا تُرجع موظفي قسم آخر
        var financeOnly = await reports.GetLeaveBalancesAsync(
            new AttendanceReportFilter { From = Day, To = Day, Department = "المالية" });
        Assert.Single(financeOnly);
        Assert.Equal("1001", financeOnly[0].EmployeeCode);
    }

    [Fact]
    public async Task Excel_Leaves_Report_Contains_Leaves_And_Balances_Sheets()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(Emp(1, "1001", "مينا فؤاد", "المالية", "محاسب"));
        ctx.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = 1, StartDate = Day, EndDate = Day.AddDays(2), LeaveType = LeaveType.Annual,
            Status = ApprovalStatus.Approved, DaysCount = 3, WorkingDays = 3m, IsPaid = true
        });
        await ctx.SaveChangesAsync();

        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var bytes = await excel.ExportLeavesAsync(new AttendanceReportFilter { From = Day, To = Day.AddDays(2) });
        using var wb = new XLWorkbook(new MemoryStream(bytes));

        Assert.Equal(new[] { "Leaves", "Balances" }, wb.Worksheets.Select(w => w.Name).ToArray());

        var leaves = wb.Worksheet("Leaves");
        Assert.Equal("م", leaves.Cell(5, 1).GetString());
        Assert.Equal("كود الموظف", leaves.Cell(5, 2).GetString());
        Assert.Equal("1001", leaves.Cell(6, 2).GetString());
        Assert.Equal("سنوية", leaves.Cell(6, 6).GetString());
        Assert.Equal("معتمد", leaves.Cell(6, 12).GetString());

        var balances = wb.Worksheet("Balances");
        Assert.Equal("المتبقي", balances.Cell(5, 9).GetString());
        Assert.Equal(21m, balances.Cell(6, 9).GetValue<decimal>());
    }

    // ===================== ملفا الدخول والخروج بترتيب مختلف =====================

    [Fact]
    public async Task Entry_And_Exit_Files_Are_Paired_By_Code_And_Time_Not_By_Row_Order()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.AddRange(
            TestDb.Employee(1, "1001", 9000m),
            TestDb.Employee(2, "1002", 6000m));
        await ctx.SaveChangesAsync();

        var import = new AttendanceImportService(ctx, NullLogger<AttendanceImportService>.Instance);

        // ملف الدخول: 1001 ثم 1002
        var entryFile = AttendanceFile("I",
            ("1001", "أ", "2024-10-07 07:35:00"),
            ("1002", "ب", "2024-10-07 07:00:00"));
        var entryAnalysis = await import.AnalyzeAsync(entryFile, "entry.xlsx");
        Assert.True(entryAnalysis.HasMapping);
        Assert.Equal(2, entryAnalysis.SuccessCount);
        var entryCommit = await import.CommitAsync(entryAnalysis, "entry.xlsx", "u1", "tester");
        Assert.Equal(2, entryCommit.Inserted);

        // ملف الخروج: بالترتيب المعاكس تمامًا (1002 ثم 1001)
        var exitFile = AttendanceFile("O",
            ("1002", "ب", "2024-10-07 15:00:00"),
            ("1001", "أ", "2024-10-07 16:00:00"));
        var exitAnalysis = await import.AnalyzeAsync(exitFile, "exit.xlsx");
        Assert.True(exitAnalysis.HasMapping);
        var exitCommit = await import.CommitAsync(exitAnalysis, "exit.xlsx", "u1", "tester");
        Assert.Equal(2, exitCommit.Inserted);

        Assert.Equal(4, await ctx.RawPunches.CountAsync());

        var processing = new AttendanceProcessingService(ctx, new SettingsService(ctx),
            NullLogger<AttendanceProcessingService>.Instance);
        await processing.ProcessAsync(Day, Day);

        var rows = await ctx.DailyAttendances.AsNoTracking().OrderBy(d => d.EmployeeId).ToListAsync();
        Assert.Equal(2, rows.Count);

        var first = rows.Single(r => r.EmployeeId == 1);
        Assert.Equal(Day.AddHours(7).AddMinutes(35), first.CheckInTime);
        Assert.Equal(Day.AddHours(16), first.CheckOutTime);
        Assert.Equal(35, first.LateMinutes);
        Assert.Equal(60, first.RawOvertimeMinutes);

        var second = rows.Single(r => r.EmployeeId == 2);
        Assert.Equal(Day.AddHours(7), second.CheckInTime);
        Assert.Equal(Day.AddHours(15), second.CheckOutTime);
        Assert.Equal(0, second.LateMinutes);
        Assert.Equal(AttendanceStatus.Present, second.Status);
    }

    [Fact]
    public async Task Re_uploading_Both_Files_Adds_No_Duplicate_Punches()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(1, "1001", 9000m));
        await ctx.SaveChangesAsync();

        var import = new AttendanceImportService(ctx, NullLogger<AttendanceImportService>.Instance);
        var entry = AttendanceFile("I", ("1001", "أ", "2024-10-07 07:00:00"));
        var exit = AttendanceFile("O", ("1001", "أ", "2024-10-07 15:00:00"));

        foreach (var (bytes, name) in new[] { (entry, "entry.xlsx"), (exit, "exit.xlsx") })
        {
            var a1 = await import.AnalyzeAsync(bytes, name);
            await import.CommitAsync(a1, name, "u1", "tester");
        }
        Assert.Equal(2, await ctx.RawPunches.CountAsync());

        // إعادة رفع الملفين نفسيهما: لا تُضاف أي بصمة جديدة
        foreach (var (bytes, name) in new[] { (entry, "entry.xlsx"), (exit, "exit.xlsx") })
        {
            var a2 = await import.AnalyzeAsync(bytes, name);
            Assert.Equal(0, a2.SuccessCount);
            Assert.Equal(1, a2.AlreadyImportedCount);
            await import.CommitAsync(a2, name, "u1", "tester");
        }
        Assert.Equal(2, await ctx.RawPunches.CountAsync());
    }

    // ===================== أمان إنشاء أول مسؤول =====================

    private static ServiceProvider IdentityProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Production_Without_Configured_Credentials_Creates_No_User_At_All()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = IdentityProvider(connection);
        await using var ctx = provider.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();

        await DbSeeder.SeedAsync(
            ctx,
            provider.GetRequiredService<RoleManager<IdentityRole>>(),
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            NullLogger.Instance,
            new AdminSeedOptions { IsDevelopment = false });

        // لا يُنشأ أي حساب بكلمة مرور افتراضية في الإنتاج
        Assert.Empty(await ctx.Users.ToListAsync());

        // الأدوار تُنشأ دائمًا لأنها لا تحمل بيانات اعتماد
        Assert.Equal(AppRoles.All.Length, await ctx.Roles.CountAsync());
    }

    [Fact]
    public async Task Development_Fallback_Creates_An_Admin_Without_Any_Static_Password()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = IdentityProvider(connection);
        await using var ctx = provider.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await DbSeeder.SeedAsync(
            ctx,
            provider.GetRequiredService<RoleManager<IdentityRole>>(),
            userManager,
            NullLogger.Instance,
            new AdminSeedOptions { IsDevelopment = true });

        var admin = await userManager.FindByEmailAsync(DbSeeder.DevelopmentAdminEmail);
        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin!, AppRoles.Admin));

        // لا توجد أي كلمة مرور ثابتة معروفة في الكود تُقبل للحساب
        Assert.False(await userManager.CheckPasswordAsync(admin!, "Admin@12345"));
        Assert.False(await userManager.CheckPasswordAsync(admin!, "admin@hr.local"));
    }

    [Fact]
    public async Task Configured_First_Admin_Is_Created_And_Granted_The_Admin_Role()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = IdentityProvider(connection);
        await using var ctx = provider.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await DbSeeder.SeedAsync(
            ctx,
            provider.GetRequiredService<RoleManager<IdentityRole>>(),
            userManager,
            NullLogger.Instance,
            new AdminSeedOptions
            {
                IsDevelopment = false,
                Email = "owner@company.com",
                Password = "FirstAdmin#2026",
                FullName = "مالك النظام"
            });

        var admin = await userManager.FindByEmailAsync("owner@company.com");
        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin!, AppRoles.Admin));
        Assert.True(await userManager.CheckPasswordAsync(admin!, "FirstAdmin#2026"));
    }

    [Fact]
    public async Task Existing_Users_Are_Not_Duplicated_Or_Overwritten_On_Reseed()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var provider = IdentityProvider(connection);
        await using var ctx = provider.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();

        await DbSeeder.SeedAsync(ctx, roleManager, userManager, NullLogger.Instance,
            new AdminSeedOptions { IsDevelopment = false, Email = "owner@company.com", Password = "FirstAdmin#2026" });
        await DbSeeder.SeedAsync(ctx, roleManager, userManager, NullLogger.Instance,
            new AdminSeedOptions { IsDevelopment = true });

        var users = await ctx.Users.ToListAsync();
        Assert.Single(users);
        Assert.Equal("owner@company.com", users[0].Email);
    }
}
