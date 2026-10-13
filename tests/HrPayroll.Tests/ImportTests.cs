using ClosedXML.Excel;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>المتطلبات 3 و4: استيراد الموظفين والبصمات مع منع التكرار والتحقق.</summary>
public class ImportTests
{
    // ===================== أدوات بناء ملفات Excel =====================

    private static byte[] BuildAttendanceFile(string fileName, params (string device, string code, string name, string type, string time)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "No";
        ws.Cell(1, 2).Value = "DeviceId";
        ws.Cell(1, 3).Value = "ID";
        ws.Cell(1, 4).Value = "Name";
        ws.Cell(1, 5).Value = "AttType";
        ws.Cell(1, 6).Value = "Att.Time";

        var r = 2;
        foreach (var (device, code, name, type, time) in rows)
        {
            ws.Cell(r, 1).Value = r - 1;
            ws.Cell(r, 2).Value = device;
            ws.Cell(r, 3).Value = code;
            ws.Cell(r, 4).Value = name;
            ws.Cell(r, 5).Value = type;
            ws.Cell(r, 6).Value = time;
            r++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] BuildEmployeeFile(params string[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Employees");
        var headers = new[] { "كود الموظف", "كود البصمة", "الاسم", "القسم", "المسمى الوظيفي", "الراتب الأساسي", "تاريخ التعيين", "الهاتف" };
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++) ws.Cell(r, c + 1).Value = row[c];
            r++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static AttendanceImportService AttendanceService(Infrastructure.Persistence.AppDbContext ctx)
        => new(ctx, NullLogger<AttendanceImportService>.Instance);

    private static EmployeeImportService EmployeeService(Infrastructure.Persistence.AppDbContext ctx)
        => new(ctx, new AuditService(ctx), NullLogger<EmployeeImportService>.Instance);

    // ===================== 16) كود بصمة غير مسجل =====================

    [Fact] // 16
    public async Task Unknown_Biometric_Code_Is_Reported_And_Not_Imported()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(code: "1001"));
        await ctx.SaveChangesAsync();

        var bytes = BuildAttendanceFile("att.xls",
            ("1", "1001", "موظف معروف", "FP", "2024-10-07 07:00:00"),
            ("1", "9999", "موظف مجهول", "FP", "2024-10-07 07:05:00"));

        var analysis = await AttendanceService(ctx).AnalyzeAsync(bytes, "att.xls");

        Assert.Equal(2, analysis.Total);
        Assert.Equal(1, analysis.SuccessCount);
        Assert.Equal(1, analysis.UnknownEmployeeCount);
        Assert.Contains(analysis.Rows, r => r.Status == ImportRowStatus.UnknownEmployee && r.DeviceEmployeeCode == "9999");

        var commit = await AttendanceService(ctx).CommitAsync(analysis, "att.xls", "u1", "tester");
        Assert.Equal(1, commit.Inserted);
        Assert.Equal(1, await ctx.RawPunches.CountAsync());
    }

    [Fact]
    public async Task Matching_Uses_Biometric_Code_Not_Name()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001"));
        await ctx.SaveChangesAsync();

        // الاسم في الملف مختلف تمامًا لكن كود الجهاز صحيح
        var bytes = BuildAttendanceFile("att.xls", ("1", "1001", "اسم مختلف تمامًا", "FP", "2024-10-07 07:00:00"));
        var analysis = await AttendanceService(ctx).AnalyzeAsync(bytes, "att.xls");

        Assert.Equal(1, analysis.SuccessCount);
        Assert.Equal(1, analysis.Rows[0].EmployeeId);
    }

    // ===================== 17) منع استيراد نفس الملف مرتين =====================

    [Fact] // 17
    public async Task Importing_The_Same_File_Twice_Inserts_Nothing_The_Second_Time()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001"));
        await ctx.SaveChangesAsync();

        var bytes = BuildAttendanceFile("att.xls",
            ("1", "1001", "أ", "FP", "2024-10-07 07:00:00"),
            ("1", "1001", "أ", "FP", "2024-10-07 15:00:00"));

        var service = AttendanceService(ctx);
        var first = await service.CommitAsync(await service.AnalyzeAsync(bytes, "att.xls"), "att.xls", "u1", "tester");
        Assert.Equal(2, first.Inserted);

        var secondAnalysis = await service.AnalyzeAsync(bytes, "att.xls");
        Assert.Equal(2, secondAnalysis.DuplicateCount);

        var second = await service.CommitAsync(secondAnalysis, "att.xls", "u1", "tester");
        Assert.Equal(0, second.Inserted);
        Assert.Equal(2, second.SkippedDuplicate);
        Assert.Equal(2, await ctx.RawPunches.CountAsync());
    }

    // ===================== 18) صفوف جديدة ليوم مستورد سابقًا =====================

    [Fact] // 18
    public async Task New_Records_For_A_Previously_Imported_Date_Are_Imported()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001"));
        await ctx.SaveChangesAsync();

        var service = AttendanceService(ctx);

        var day1 = BuildAttendanceFile("day1.xls", ("1", "1001", "أ", "FP", "2024-10-07 07:00:00"));
        await service.CommitAsync(await service.AnalyzeAsync(day1, "day1.xls"), "day1.xls", "u1", "tester");
        Assert.Equal(1, await ctx.RawPunches.CountAsync());

        // ملف أحدث لنفس التاريخ ويحتوي بصمة جديدة
        var day1b = BuildAttendanceFile("day1b.xls", ("1", "1001", "أ", "FP", "2024-10-07 15:10:00"));
        var result = await service.CommitAsync(await service.AnalyzeAsync(day1b, "day1b.xls"), "day1b.xls", "u1", "tester");

        Assert.Equal(1, result.Inserted);
        Assert.Equal(2, await ctx.RawPunches.CountAsync());
    }

    // ============ 15/17) حماية التكرار لا تعتمد على صيغة الـ Hash وحدها ============

    [Fact]
    public async Task A_Punch_Stored_With_A_Legacy_Hash_Format_Is_Still_Treated_As_Already_Imported()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001"));
        await ctx.SaveChangesAsync();

        // بصمة محفوظة بصيغة Hash قديمة/مختلفة — كما يحدث إذا تغيّرت ثقافة/تقويم الخادم
        // (كان الهاش يشمل تاريخًا منسّقًا بثقافة الخادم: هجري مقابل ميلادي).
        ctx.RawPunches.Add(new RawPunch
        {
            DeviceId = "1",
            DeviceEmployeeCode = "1001",
            RawEmployeeName = "أ",
            AttType = "FP",
            PunchTime = new DateTime(2024, 10, 7, 7, 0, 0),
            DedupHash = "LEGACY-HASH-FORMAT-NOT-CANONICAL",
            IsValid = true
        });
        await ctx.SaveChangesAsync();

        var bytes = BuildAttendanceFile("att.xls", ("1", "1001", "أ", "FP", "2024-10-07 07:00:00"));
        var service = AttendanceService(ctx);
        var analysis = await service.AnalyzeAsync(bytes, "att.xls");

        Assert.Equal(0, analysis.SuccessCount);          // لا شيء جديد
        Assert.Equal(1, analysis.AlreadyImportedCount);  // يُعرض في المعاينة كمستورد سابقًا
        Assert.Equal(1, analysis.DuplicateCount);

        var commit = await service.CommitAsync(analysis, "att.xls", "u1", "tester");
        Assert.Equal(0, commit.Inserted);
        Assert.Equal(1, await ctx.RawPunches.CountAsync());
    }

    [Fact]
    public void Duplicate_Hash_Is_Independent_Of_The_Server_Culture()
    {
        var time = new DateTime(2026, 1, 5, 16, 0, 0);
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA"); // تقويم هجري
            var hijri = AttendanceImportService.ComputeHash("1", "1001", time, "FP");

            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var invariant = AttendanceImportService.ComputeHash("1", "1001", time, "FP");

            Assert.Equal(invariant, hijri);
            Assert.Equal(AttendanceImportService.NaturalKey("1001", time, "FP"),
                         AttendanceImportService.NaturalKey(" 1001 ", time, "fp"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Invalid_Rows_And_Missing_Columns_Are_Reported()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001"));
        await ctx.SaveChangesAsync();

        var bytes = BuildAttendanceFile("att.xls",
            ("1", "1001", "أ", "FP", "2024-10-07 07:00:00"),
            ("1", "1001", "أ", "FP", "نص غير صالح"),
            ("1", "1001", "أ", "FP", "2024-10-07 07:00:00")); // مكرر داخل الملف

        var analysis = await AttendanceService(ctx).AnalyzeAsync(bytes, "att.xls");

        Assert.Equal(3, analysis.Total);
        Assert.Equal(1, analysis.SuccessCount);
        Assert.Equal(1, analysis.DuplicateCount);
        Assert.Equal(1, analysis.InvalidCount);
    }

    // ===================== استيراد الموظفين =====================

    [Fact]
    public async Task Employee_Import_Preview_Validates_And_Detects_Duplicates()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001", salary: 9000m));
        await ctx.SaveChangesAsync();

        var bytes = BuildEmployeeFile(
            new[] { "2001", "2001", "أحمد علي", "المالية", "محاسب", "9000", "2024-01-15", "01000000001" },
            new[] { "2002", "2002", "منى حسن", "المالية", "محاسب", "7500", "2024-02-01", "" },
            new[] { "2002", "2003", "مكرر كود", "المالية", "محاسب", "8000", "2024-02-01", "" },
            new[] { "2004", "2004", "بدون راتب", "المالية", "محاسب", "", "2024-02-01", "" },
            new[] { "1001", "1001", "موظف موجود", "المالية", "محاسب", "9500", "2022-01-01", "" });

        var analysis = await EmployeeService(ctx).AnalyzeAsync(bytes, "employees.xlsx");

        Assert.True(analysis.Mapping!.IsUsable);
        Assert.Equal(5, analysis.Total);
        Assert.Equal(2, analysis.NewCount);
        Assert.Equal(1, analysis.DuplicateCount);
        Assert.Equal(1, analysis.InvalidCount);
        Assert.Equal(1, analysis.UpdateCount);
        Assert.Contains(analysis.Rows, r => r.Messages.Any(m => m.Contains("مفقود")));
    }

    [Fact]
    public async Task Employee_Import_CreateOnly_Does_Not_Touch_Existing_Employees()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001", salary: 9000m));
        await ctx.SaveChangesAsync();

        var bytes = BuildEmployeeFile(
            new[] { "2001", "2001", "أحمد علي", "المالية", "محاسب", "9000", "2024-01-15", "" },
            new[] { "1001", "1001", "اسم جديد مختلف", "المالية", "مدير", "20000", "2022-01-01", "" });

        var service = EmployeeService(ctx);
        var analysis = await service.AnalyzeAsync(bytes, "employees.xlsx");
        var result = await service.CommitAsync(analysis, EmployeeImportMode.CreateOnly, "employees.xlsx", "u1", "tester");

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped);

        var existing = await ctx.Employees.SingleAsync(e => e.EmployeeCode == "1001");
        Assert.Equal(9000m, existing.BasicSalary);           // لم يُستبدل الراتب
        Assert.NotEqual("اسم جديد مختلف", existing.FullName); // ولم يُستبدل الاسم
    }

    [Fact]
    public async Task Employee_Import_CreateAndUpdate_Updates_With_Audit_Trail()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Employees.Add(TestDb.Employee(code: "1001", salary: 9000m));
        await ctx.SaveChangesAsync();

        var bytes = BuildEmployeeFile(new[] { "1001", "1001", "الاسم المحدَّث", "المالية", "مدير", "12000", "2022-01-01", "" });

        var service = EmployeeService(ctx);
        var analysis = await service.AnalyzeAsync(bytes, "employees.xlsx");
        var result = await service.CommitAsync(analysis, EmployeeImportMode.CreateAndUpdate, "employees.xlsx", "u1", "tester");

        Assert.Equal(1, result.Updated);
        var updated = await ctx.Employees.SingleAsync(e => e.EmployeeCode == "1001");
        Assert.Equal(12000m, updated.BasicSalary);
        Assert.Equal("الاسم المحدَّث", updated.FullName);

        var audit = await ctx.AuditLogs.SingleAsync(a => a.Action == "تحديث موظف من ملف Excel");
        Assert.Contains("9000", audit.OldValue);
        Assert.Equal("tester", audit.UserName);
    }

    [Fact]
    public async Task Employee_Import_Creates_Missing_Departments_And_Job_Titles()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();

        var bytes = BuildEmployeeFile(new[] { "3001", "3001", "سارة", "قسم جديد تمامًا", "وظيفة جديدة", "8000", "2024-03-01", "" });
        var service = EmployeeService(ctx);
        var analysis = await service.AnalyzeAsync(bytes, "employees.xlsx");
        await service.CommitAsync(analysis, EmployeeImportMode.CreateOnly, "employees.xlsx", "u1", "tester");

        Assert.True(await ctx.Departments.AnyAsync(d => d.Name == "قسم جديد تمامًا"));
        Assert.True(await ctx.JobTitles.AnyAsync(j => j.Name == "وظيفة جديدة"));

        var emp = await ctx.Employees.SingleAsync(e => e.EmployeeCode == "3001");
        Assert.Equal("قسم جديد تمامًا", emp.Department);
        Assert.Equal("وظيفة جديدة", emp.JobTitle);
    }

    [Fact]
    public async Task Employee_Import_Records_The_Import_Batch_To_Prevent_Silent_Repeats()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();

        var bytes = BuildEmployeeFile(new[] { "4001", "4001", "خالد", "المالية", "محاسب", "6000", "2024-04-01", "" });
        var service = EmployeeService(ctx);

        var first = await service.AnalyzeAsync(bytes, "employees.xlsx");
        Assert.False(first.AlreadyImported);
        await service.CommitAsync(first, EmployeeImportMode.CreateOnly, "employees.xlsx", "u1", "tester");

        var second = await service.AnalyzeAsync(bytes, "employees.xlsx");
        Assert.True(second.AlreadyImported);
        Assert.NotNull(second.PreviousImportSummary);
        Assert.Equal(1, await ctx.Employees.CountAsync(e => e.EmployeeCode == "4001"));
    }
}
