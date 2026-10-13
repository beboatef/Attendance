using ClosedXML.Excel;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>
/// المتطلب 13: تصدير كشف المرتبات بنفس أعمدة الجدول المعروض وبالقيم المحفوظة والمعتمدة،
/// وليس بقيم افتراضية.
/// </summary>
public class PayrollSheetExportTests
{
    private static readonly DateTime PeriodStart = new(2024, 10, 1);

    [Fact]
    public async Task Payroll_Export_Contains_All_Sheet_Columns_And_The_Saved_Values()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        var emp = TestDb.Employee(salary: 9000m);
        emp.AnnualLeaveEntitlement = 21m;
        emp.AnnualLeaveBalance = 21m;
        ctx.Employees.Add(emp);
        await ctx.SaveChangesAsync();

        AddDays(ctx, 15, AttendanceStatus.Present, withPunch: true);
        AddDays(ctx, 5, AttendanceStatus.Absent, withPunch: false);
        await ctx.SaveChangesAsync();

        var settings = new SettingsService(ctx);
        var audit = new AuditService(ctx);
        var payroll = new PayrollService(ctx, settings, audit, NullLogger<PayrollService>.Instance);
        var result = await payroll.GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        // تعديل يدوي كما يفعل المحاسب من الجدول: غياب 5 → 2 وتحويل يومين إلى إجازة سنوية + مكافأة
        var sheet = new PayrollSheetService(ctx, settings, audit);
        var saved = await sheet.SaveAsync(result.PayrollRunId, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 2m, AnnualLeaveDays = 2m, Bonuses = 300m }
        }, false, "tester", "u1");
        Assert.True(saved.Rows[0].Saved, saved.Rows[0].Error);

        ctx.ChangeTracker.Clear();
        var stored = await ctx.PayrollItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);

        var excel = new ExcelExportService(ctx, settings, new ReportService(ctx, settings));
        var bytes = await excel.ExportPayrollAsync(result.PayrollRunId, default);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Payroll");

        // العناوين (صف 5) مطابقة لأعمدة الكشف المعروض
        Assert.Equal("كود الموظف", ws.Cell(5, 2).GetString());
        Assert.Equal("الغياب من البصمة", ws.Cell(5, 9).GetString());
        Assert.Equal("الغياب المعتمد", ws.Cell(5, 10).GetString());
        Assert.Equal("أيام الإجازة السنوية", ws.Cell(5, 11).GetString());
        Assert.Equal("رصيد الإجازة السنوية", ws.Cell(5, 12).GetString());
        Assert.Equal("ساعات الإضافي المعتمدة", ws.Cell(5, 16).GetString());
        Assert.Equal("صافي الراتب", ws.Cell(5, 29).GetString());
        Assert.Equal("حالة المراجعة", ws.Cell(5, 30).GetString());

        // القيم المصدَّرة = القيم المحفوظة بعد التعديل (وليس الافتراضية)
        Assert.Equal(stored.OriginalAbsenceDays, ws.Cell(6, 9).GetValue<decimal>());
        Assert.Equal(5m, ws.Cell(6, 9).GetValue<decimal>());      // الغياب من البصمة يبقى 5
        Assert.Equal(2m, ws.Cell(6, 10).GetValue<decimal>());     // الغياب المعتمد
        Assert.Equal(2m, ws.Cell(6, 11).GetValue<decimal>());     // الإجازة السنوية
        Assert.Equal(19m, ws.Cell(6, 12).GetValue<decimal>());    // الرصيد بعد خصم يومين
        Assert.Equal(300m, ws.Cell(6, 18).GetValue<decimal>());   // المكافآت
        Assert.Equal(stored.NetSalary, ws.Cell(6, 29).GetValue<decimal>());

        // الحساب: 9000 + 300 − (2 × 300) = 8700
        Assert.Equal(8700m, stored.NetSalary);
        Assert.Equal(600m, stored.AbsenceDeduction);
        Assert.Equal("مُعدَّل يدويًا", ws.Cell(6, 30).GetString());
    }

    private static void AddDays(AppDbContext ctx, int count, AttendanceStatus status, bool withPunch)
    {
        var taken = ctx.DailyAttendances.Local.Where(d => d.EmployeeId == 1)
            .Select(d => d.WorkDate.Date).ToHashSet();
        var added = 0;
        for (var offset = 0; added < count; offset++)
        {
            var date = PeriodStart.AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Friday) continue;
            if (!taken.Add(date)) continue;
            added++;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1,
                WorkDate = date,
                ShiftId = 1,
                ShiftStart = date.AddHours(7),
                ShiftEnd = date.AddHours(15),
                CheckInTime = withPunch ? date.AddHours(7) : null,
                CheckOutTime = withPunch ? date.AddHours(15) : null,
                Status = status,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
    }
}
