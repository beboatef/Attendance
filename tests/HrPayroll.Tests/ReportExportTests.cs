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
            Assert.Equal(item.NetSalary, ws.Cell(row, 31).GetValue<decimal>());
            Assert.Equal(item.GrossEarnings, ws.Cell(row, 29).GetValue<decimal>());
            Assert.Equal(item.TotalDeductions, ws.Cell(row, 30).GetValue<decimal>());
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
