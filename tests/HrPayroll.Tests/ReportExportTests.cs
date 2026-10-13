using ClosedXML.Excel;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>المتطلب 28: تناسق التقارير والتصدير مع الأرقام المخزّنة، وتوليد ملفات Excel و PDF.</summary>
public class ReportExportTests
{
    private static readonly DateTime Day = TestDb.Monday;

    private static async Task<(TestDb Db, AppDbContext Ctx)> SeedAsync()
    {
        var db = new TestDb();
        var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.AddRange(
            new Employee
            {
                Id = 1, EmployeeCode = "1001", DeviceEmployeeCode = "1001", FullName = "مينا فؤاد",
                Department = "المالية", JobTitle = "محاسب", BasicSalary = 9000m, HireDate = new DateTime(2022, 1, 1),
                EmploymentStatus = EmploymentStatus.Active, IsActive = true, StandardWorkHours = 8m
            },
            new Employee
            {
                Id = 2, EmployeeCode = "1002", DeviceEmployeeCode = "1002", FullName = "سارة علي",
                Department = "الموارد البشرية", JobTitle = "أخصائي", BasicSalary = 6000m, HireDate = new DateTime(2023, 1, 1),
                EmploymentStatus = EmploymentStatus.Active, IsActive = true, StandardWorkHours = 8m
            },
            new Employee
            {
                Id = 3, EmployeeCode = "1003", DeviceEmployeeCode = "1003", FullName = "أحمد سمير",
                Department = "المالية", JobTitle = "محاسب", BasicSalary = 7500m, HireDate = new DateTime(2023, 6, 1),
                EmploymentStatus = EmploymentStatus.Active, IsActive = true, StandardWorkHours = 8m
            });

        ctx.DailyAttendances.AddRange(
            new DailyAttendance
            {
                EmployeeId = 1, WorkDate = Day, ShiftId = 1, ShiftStart = Day.AddHours(7), ShiftEnd = Day.AddHours(15),
                CheckInTime = Day.AddHours(7).AddMinutes(35), CheckOutTime = Day.AddHours(16),
                LateMinutes = 35, RawOvertimeMinutes = 60, Status = AttendanceStatus.Late,
                PunchReviewStatus = ClassificationStatus.Classified
            },
            new DailyAttendance
            {
                EmployeeId = 2, WorkDate = Day, ShiftId = 1, ShiftStart = Day.AddHours(7), ShiftEnd = Day.AddHours(15),
                CheckInTime = Day.AddHours(7), CheckOutTime = Day.AddHours(15), Status = AttendanceStatus.Present,
                PunchReviewStatus = ClassificationStatus.Classified
            },
            new DailyAttendance
            {
                EmployeeId = 3, WorkDate = Day, ShiftId = 1, ShiftStart = Day.AddHours(7), ShiftEnd = Day.AddHours(15),
                Status = AttendanceStatus.Absent, PunchReviewStatus = ClassificationStatus.Classified
            });

        await ctx.SaveChangesAsync();
        return (db, ctx);
    }

    // ===================== 28) فلترة الأقسام وتناسق التقارير =====================

    [Fact] // 28
    public async Task Daily_Report_Respects_Department_And_Employee_Filters()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));

        var all = await reports.GetDailyAttendanceAsync(new AttendanceReportFilter { From = Day, To = Day });
        Assert.Equal(3, all.Count);

        var finance = await reports.GetDailyAttendanceAsync(new AttendanceReportFilter { From = Day, To = Day, Department = "المالية" });
        Assert.Equal(2, finance.Count);
        Assert.All(finance, r => Assert.Equal("المالية", r.Department));

        var byEmployee = await reports.GetDailyAttendanceAsync(new AttendanceReportFilter { From = Day, To = Day, EmployeeId = 2 });
        Assert.Single(byEmployee);
        Assert.Equal("1002", byEmployee[0].EmployeeCode);
    }

    [Fact] // 28
    public async Task Report_Rows_Match_The_Stored_Daily_Attendance_Values()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));

        var rows = await reports.GetDailyAttendanceAsync(new AttendanceReportFilter { From = Day, To = Day });
        var stored = await ctx.DailyAttendances.AsNoTracking().OrderBy(d => d.EmployeeId).ToListAsync();

        foreach (var s in stored)
        {
            var row = rows.Single(r => r.EmployeeId == s.EmployeeId);
            Assert.Equal(s.LateMinutes, row.LateMinutes);
            Assert.Equal(s.RawOvertimeMinutes, row.RawOvertimeMinutes);
            Assert.Equal(s.ApprovedOvertimeMinutes, row.ApprovedOvertimeMinutes);
            Assert.Equal(s.Status, row.Status);
            Assert.Equal(s.CheckInTime, row.CheckIn);
            Assert.Equal(s.CheckOutTime, row.CheckOut);
        }
    }

    [Fact] // 28
    public async Task Department_Report_Aggregates_Correctly()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));

        var departments = await reports.GetDepartmentReportAsync(new AttendanceReportFilter { From = Day, To = Day });
        Assert.Equal(2, departments.Count);

        var finance = departments.Single(d => d.Department == "المالية");
        Assert.Equal(2, finance.Total);
        Assert.Equal(1, finance.Present);
        Assert.Equal(1, finance.Absent);
        Assert.Equal(1, finance.Late);
        Assert.Equal(50m, finance.PresentRate);

        var hr = departments.Single(d => d.Department == "الموارد البشرية");
        Assert.Equal(1, hr.Total);
        Assert.Equal(1, hr.Present);
        Assert.Equal(0, hr.Absent);
    }

    [Fact]
    public async Task Overtime_And_Absence_Reports_Consume_The_Same_Source()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));

        var overtime = await reports.GetOvertimeAsync(new AttendanceReportFilter { From = Day, To = Day }, onlyWithOvertime: true);
        Assert.Single(overtime);
        Assert.Equal(60, overtime[0].RawMinutes);
        Assert.Equal(0, overtime[0].ApprovedMinutes);

        var absence = await reports.GetAbsenceAndLatenessAsync(new AttendanceReportFilter { From = Day, To = Day });
        Assert.Equal(2, absence.Count);   // المتأخر + الغائب
        Assert.Contains(absence, a => a.Status == AttendanceStatus.Absent);
        Assert.Contains(absence, a => a.LateMinutes == 35);
    }

    // ===================== تصدير Excel =====================

    [Fact]
    public async Task Excel_Daily_Export_Contains_Exactly_The_Reported_Rows()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var filter = new AttendanceReportFilter { From = Day, To = Day };
        var rows = await reports.GetDailyAttendanceAsync(filter);
        var bytes = await excel.ExportDailyAttendanceAsync(filter);

        Assert.True(bytes.Length > 0);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Attendance");
        var usedRows = ws.RowsUsed().Count();

        // 3 صفوف عناوين + صف العناوين + عدد الصفوف المصدرة
        Assert.Equal(4 + rows.Count, usedRows);
        Assert.Equal("كود الموظف", ws.Cell(5, 2).GetString());
    }

    [Fact]
    public async Task Excel_Payroll_Export_Matches_Stored_Payroll_Items()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;

        for (var i = 0; i < 20; i++)
        {
            // الشهر المستهدف للكشف غير الشهر المستخدم في بيانات الحضور المرجعية
            var date = new DateTime(2024, 12, 1).AddDays(i);
            if (date.DayOfWeek == DayOfWeek.Friday) continue;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1, WorkDate = date, ShiftId = 1, ShiftStart = date.AddHours(7), ShiftEnd = date.AddHours(15),
                CheckInTime = date.AddHours(7), CheckOutTime = date.AddHours(15), Status = AttendanceStatus.Present,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
        await ctx.SaveChangesAsync();

        var payroll = new PayrollService(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);
        var run = await payroll.GenerateAsync(2024, 12, "tester", "u1");

        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var items = await ctx.PayrollItems.AsNoTracking().Where(i => i.PayrollRunId == run.PayrollRunId).ToListAsync();
        var bytes = await excel.ExportPayrollAsync(run.PayrollRunId);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Payroll");
        Assert.Equal("كود الموظف", ws.Cell(5, 2).GetString());

        // كل موظف في الكشف له سطر مُصدَّر يحمل نفس القيم المخزّنة
        Assert.Equal(items.Count, ws.LastRowUsed()!.RowNumber() - 5);
        foreach (var item in items)
        {
            var row = Enumerable.Range(6, items.Count)
                .First(r => ws.Cell(r, 2).GetString() == item.EmployeeCode);
            // ترتيب أعمدة الكشف: 27 إجمالي الاستحقاقات، 28 إجمالي الخصومات، 29 صافي الراتب
            Assert.Equal(item.GrossEarnings, ws.Cell(row, 27).GetValue<decimal>());
            Assert.Equal(item.TotalDeductions, ws.Cell(row, 28).GetValue<decimal>());
            Assert.Equal(item.NetSalary, ws.Cell(row, 29).GetValue<decimal>());
            Assert.Equal(item.OriginalAbsenceDays, ws.Cell(row, 9).GetValue<decimal>());
            Assert.Equal(item.AbsenceDays, ws.Cell(row, 10).GetValue<decimal>());
            Assert.Equal(item.AnnualLeaveDays, ws.Cell(row, 11).GetValue<decimal>());
        }
    }

    [Fact]
    public async Task Excel_Payslip_Export_Is_Generated()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        for (var i = 0; i < 20; i++)
        {
            // الشهر المستهدف للكشف غير الشهر المستخدم في بيانات الحضور المرجعية
            var date = new DateTime(2024, 12, 1).AddDays(i);
            if (date.DayOfWeek == DayOfWeek.Friday) continue;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1, WorkDate = date, ShiftId = 1, ShiftStart = date.AddHours(7), ShiftEnd = date.AddHours(15),
                CheckInTime = date.AddHours(7), CheckOutTime = date.AddHours(15), Status = AttendanceStatus.Present,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
        await ctx.SaveChangesAsync();

        var payroll = new PayrollService(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);
        var run = await payroll.GenerateAsync(2024, 12, "tester", "u1");
        var item = await ctx.PayrollItems.AsNoTracking().SingleAsync(i => i.PayrollRunId == run.PayrollRunId && i.EmployeeId == 1);

        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var bytes = await excel.ExportPayslipAsync(item.Id);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.NotNull(wb.Worksheet("Payslip"));
    }

    [Fact] // 11) صحة تصدير Entry/Exit والربط بالسجلات الأصلية
    public async Task Entry_Sheet_Preserves_Link_To_Raw_Punch()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(new Employee
        {
            Id = 1, EmployeeCode = "1001", DeviceEmployeeCode = "1001", FullName = "مينا فؤاد",
            Department = "المالية", JobTitle = "محاسب", BasicSalary = 9000m, HireDate = new DateTime(2022, 1, 1),
            EmploymentStatus = EmploymentStatus.Active, IsActive = true, StandardWorkHours = 8m
        });

        ctx.RawPunches.AddRange(
            new RawPunch
            {
                Id = 11, DeviceId = "DEV-01", DeviceEmployeeCode = "1001", RawEmployeeName = "مينا فؤاد",
                AttType = "I", PunchTime = Day.AddHours(7), RawPunchTimeText = "07:00", RowNumber = 42,
                DedupHash = "entry-hash", EmployeeId = 1
            },
            new RawPunch
            {
                Id = 12, DeviceId = "DEV-01", DeviceEmployeeCode = "1001", RawEmployeeName = "مينا فؤاد",
                AttType = "O", PunchTime = Day.AddHours(16), RawPunchTimeText = "16:00", RowNumber = 43,
                DedupHash = "exit-hash", EmployeeId = 1
            });

        ctx.AttendanceEvents.AddRange(
            new AttendanceEvent
            {
                EmployeeId = 1, RawPunchId = 11, ShiftId = 1, WorkDate = Day,
                EventType = AttendanceEventType.Entry, EventTime = Day.AddHours(7),
                ClassificationStatus = ClassificationStatus.Classified,
                ClassificationSource = ClassificationSource.ExplicitDeviceType
            },
            new AttendanceEvent
            {
                EmployeeId = 1, RawPunchId = 12, ShiftId = 1, WorkDate = Day,
                EventType = AttendanceEventType.Exit, EventTime = Day.AddHours(16),
                ClassificationStatus = ClassificationStatus.Classified,
                ClassificationSource = ClassificationSource.ExplicitDeviceType
            });
        await ctx.SaveChangesAsync();

        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);
        var filter = new AttendanceReportFilter { From = Day, To = Day };

        var entryBytes = await excel.ExportEventsAsync(filter, AttendanceEventType.Entry);
        using var entryWb = new XLWorkbook(new MemoryStream(entryBytes));
        var entry = entryWb.Worksheet("Entry");
        Assert.Single(entryWb.Worksheets);                       // ورقة الدخول فقط
        Assert.Equal("كود الموظف", entry.Cell(5, 2).GetString());
        Assert.Equal("1001", entry.Cell(6, 2).GetString());
        Assert.Equal("DEV-01", entry.Cell(6, 6).GetString());    // رقم الجهاز من السجل الأصلي

        // رقم السجل الأصلي يشير فعلًا إلى صف البصمة الخام نفسه
        var linkedId = entry.Cell(6, 7).GetValue<int>();
        var linkedRaw = await ctx.RawPunches.AsNoTracking().SingleAsync(x => x.Id == linkedId);
        Assert.Equal(42, linkedRaw.RowNumber);
        Assert.Equal(Day.AddHours(7), linkedRaw.PunchTime);

        var exitBytes = await excel.ExportEventsAsync(filter, AttendanceEventType.Exit);
        using var exitWb = new XLWorkbook(new MemoryStream(exitBytes));
        var exit = exitWb.Worksheet("Exit");
        Assert.Single(exitWb.Worksheets);
        Assert.Equal(12, exit.Cell(6, 7).GetValue<int>());
    }

    [Fact] // 11) الملف الشامل يحوي كل الأوراق المطلوبة بنفس ترتيبها
    public async Task Full_Export_Contains_All_Required_Sheets()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var bytes = await excel.ExportFullWorkbookAsync(new AttendanceReportFilter { From = Day, To = Day });
        using var wb = new XLWorkbook(new MemoryStream(bytes));

        var expected = new[]
        {
            "Raw Punches", "Entry", "Exit", "Daily Attendance", "Late Report", "Overtime Report", "Payroll", "Exceptions"
        };
        Assert.Equal(expected, wb.Worksheets.Select(w => w.Name).ToArray());

        // ورقة الحضور اليومي تحمل نفس الصفوف الثلاثة المخزّنة
        var daily = wb.Worksheet("Daily Attendance");
        Assert.Equal("كود الموظف", daily.Cell(5, 2).GetString());
        Assert.Equal(4 + 3, daily.RowsUsed().Count());

        // المتأخر 35 دقيقة يظهر في تقرير التأخير بنفس القيمة المخزّنة
        var late = wb.Worksheet("Late Report");
        Assert.Equal(35, late.Cell(6, 8).GetValue<int>());
        Assert.Equal("1001", late.Cell(6, 2).GetString());

        // الصافي في ورقة الرواتب يبقى مطابقًا للمخزّن عند وجود كشف
        Assert.Equal("لا يوجد كشف رواتب", wb.Worksheet("Payroll").Cell(5, 1).GetString());
    }

    [Fact]
    public async Task Employee_Import_Template_Contains_All_Required_Headers()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        var reports = new ReportService(ctx, new SettingsService(ctx));
        var excel = new ExcelExportService(ctx, new SettingsService(ctx), reports);

        var bytes = await excel.BuildEmployeeImportTemplateAsync();
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Employees");

        Assert.Equal("كود الموظف", ws.Cell(1, 1).GetString());
        Assert.Equal("الراتب الأساسي", ws.Cell(1, 6).GetString());
        Assert.Equal("تاريخ التعيين", ws.Cell(1, 8).GetString());
    }

    // ===================== تصدير PDF =====================

    [Fact]
    public async Task Pdf_Payroll_And_Payslip_Are_Valid_Pdf_Files()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        for (var i = 0; i < 20; i++)
        {
            var date = new DateTime(2024, 12, 1).AddDays(i);
            if (date.DayOfWeek == DayOfWeek.Friday) continue;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1, WorkDate = date, ShiftId = 1, ShiftStart = date.AddHours(7), ShiftEnd = date.AddHours(15),
                CheckInTime = date.AddHours(7), CheckOutTime = date.AddHours(15), Status = AttendanceStatus.Present,
                RawOvertimeMinutes = 30, ApprovedOvertimeMinutes = 30,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
        await ctx.SaveChangesAsync();

        var payroll = new PayrollService(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);
        var run = await payroll.GenerateAsync(2024, 12, "tester", "u1");
        var item = await ctx.PayrollItems.AsNoTracking().SingleAsync(i => i.PayrollRunId == run.PayrollRunId && i.EmployeeId == 1);

        var settings = new SettingsService(ctx);
        var reports = new ReportService(ctx, settings);
        var pdf = new PdfExportService(reports, settings);

        var payslip = await pdf.BuildPayslipAsync(item.Id);
        Assert.True(payslip.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(payslip, 0, 4));

        var report = await pdf.BuildPayrollReportAsync(run.PayrollRunId);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(report, 0, 4));

        var attendance = await pdf.BuildAttendanceReportAsync(new AttendanceReportFilter { From = Day, To = Day });
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(attendance, 0, 4));
    }
}
