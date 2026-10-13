using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>
/// المتطلبات 6 و7 و9 و11: تعديل الغياب والإجازات والمكافآت والخصومات مباشرة من كشف المرتبات
/// مع إعادة الحساب على السيرفر، وخصم رصيد الإجازة السنوية مرة واحدة فقط.
/// </summary>
public class PayrollSheetEditTests
{
    private static readonly DateTime PeriodStart = new(2024, 10, 1);

    private static PayrollService Payroll(AppDbContext ctx)
        => new(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);

    private static PayrollSheetService Sheet(AppDbContext ctx)
        => new(ctx, new SettingsService(ctx), new AuditService(ctx));

    private static async Task<(TestDb Db, AppDbContext Ctx)> SeedAsync(
        decimal salary = 9000m, decimal entitlement = 21m, decimal balance = 21m, decimal carried = 0m,
        int absentDays = 5, int presentDays = 15)
    {
        var db = new TestDb();
        var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        var emp = TestDb.Employee(salary: salary);
        emp.AnnualLeaveEntitlement = entitlement;
        emp.AnnualLeaveCarriedOver = carried;
        emp.AnnualLeaveBalance = balance;
        ctx.Employees.Add(emp);
        await ctx.SaveChangesAsync();

        AddDays(ctx, presentDays, AttendanceStatus.Present, withPunch: true);
        AddDays(ctx, absentDays, AttendanceStatus.Absent, withPunch: false);
        await ctx.SaveChangesAsync();
        return (db, ctx);
    }

    private static void AddDays(AppDbContext ctx, int count, AttendanceStatus status, bool withPunch, int startOffset = 0)
    {
        var taken = ctx.DailyAttendances.Local.Where(d => d.EmployeeId == 1)
            .Select(d => d.WorkDate.Date).ToHashSet();
        var added = 0;
        for (var offset = startOffset; added < count; offset++)
        {
            var date = PeriodStart.AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Friday) continue;      // الجمعة يوم راحة مدفوعة
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

    private static async Task<(PayrollRun Run, PayrollItem Item)> GenerateAsync(AppDbContext ctx)
    {
        var result = await Payroll(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var run = await ctx.PayrollRuns.SingleAsync(r => r.Id == result.PayrollRunId);
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == run.Id);
        return (run, item);
    }

    // ===== 1..4: تعديل الغياب وتحويله إلى إجازة سنوية =====
    [Fact]
    public async Task Absence_Edit_And_Annual_Leave_Conversion_Deducts_Two_Days_Only()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;

        var (run, item) = await GenerateAsync(ctx);
        Assert.Equal(5m, item.AbsenceDays);
        Assert.Equal(5m, item.OriginalAbsenceDays);
        Assert.Equal(1500m, item.AbsenceDeduction);   // 5 × 300
        Assert.Equal(7500m, item.NetSalary);

        var res = await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 2m, AnnualLeaveDays = 2m }
        }, allowOverdraft: false, "tester", "u1");

        var row = Assert.Single(res.Rows);
        Assert.True(row.Saved, row.Error);

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.Include(i => i.Employee).SingleAsync(i => i.Id == item.Id);

        // الغياب الأصلي من البصمة يبقى محفوظًا (لا يُستبدل)
        Assert.Equal(5m, after.OriginalAbsenceDays);
        Assert.Equal(2m, after.AbsenceDays);
        Assert.Equal(2m, after.AnnualLeaveDays);
        // الخصم عن يومين فقط وليس خمسة
        Assert.Equal(600m, after.AbsenceDeduction);
        // الإجازة السنوية لا تُخصم كغياب: لا يوجد خصم إضافي
        Assert.Equal(0m, after.UnpaidLeaveDeduction);
        Assert.Equal(8400m, after.NetSalary);          // 9000 − 600
        // الرصيد السنوي نُقص يومين فقط
        Assert.Equal(19m, after.Employee!.AnnualLeaveBalance);
        Assert.Equal(2m, after.AnnualLeaveBalanceApplied);
    }

    [Fact]
    public async Task Saving_The_Same_Annual_Leave_Twice_Does_Not_Deduct_The_Balance_Twice()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);
        var sheet = Sheet(ctx);

        for (var i = 0; i < 3; i++)
        {
            var res = await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
            {
                new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 2m, AnnualLeaveDays = 2m }
            }, false, "tester", "u1");
            Assert.True(res.Rows[0].Saved, res.Rows[0].Error);
        }

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.Include(i => i.Employee).SingleAsync(i => i.Id == item.Id);
        Assert.Equal(19m, after.Employee!.AnnualLeaveBalance);   // 21 − 2 فقط
        Assert.Equal(2m, after.AnnualLeaveBalanceApplied);
    }

    [Fact]
    public async Task Reducing_Annual_Leave_Returns_Days_To_The_Balance()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);
        var sheet = Sheet(ctx);

        await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, AnnualLeaveDays = 4m }
        }, false, "tester", "u1");

        await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, AnnualLeaveDays = 1m }
        }, false, "tester", "u1");

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.Include(i => i.Employee).SingleAsync(i => i.Id == item.Id);
        Assert.Equal(20m, after.Employee!.AnnualLeaveBalance);
        Assert.Equal(1m, after.AnnualLeaveDays);
    }

    [Fact]
    public async Task Insufficient_Annual_Balance_Requires_Confirmation_Then_Can_Be_Overridden()
    {
        var (db, ctx) = await SeedAsync(balance: 1m);
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);
        var sheet = Sheet(ctx);

        var blocked = await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, AnnualLeaveDays = 2m }
        }, allowOverdraft: false, "tester", "u1");

        Assert.False(blocked.Rows[0].Saved);
        Assert.True(blocked.Rows[0].RequiresConfirmation);
        Assert.Contains("الإجازة السنوية", blocked.Rows[0].Error!);
        Assert.Contains(blocked.Rows[0].Warnings, w => w.Contains("رصيد الإجازة السنوية"));

        ctx.ChangeTracker.Clear();
        Assert.Equal(0m, (await ctx.PayrollItems.SingleAsync(i => i.Id == item.Id)).AnnualLeaveDays);

        var forced = await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, AnnualLeaveDays = 2m }
        }, allowOverdraft: true, "tester", "u1");
        Assert.True(forced.Rows[0].Saved, forced.Rows[0].Error);

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.Include(i => i.Employee).SingleAsync(i => i.Id == item.Id);
        Assert.Equal(2m, after.AnnualLeaveDays);
        Assert.Equal(-1m, after.Employee!.AnnualLeaveBalance);
        Assert.True(forced.Rows[0].NeedsReview);
    }

    // ===== 5 و6 و7: المكافآت والخصومات والإضافي =====
    [Fact]
    public async Task Bonus_And_Allowance_Edits_Recompute_Gross_And_Net()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        var res = await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, Bonuses = 300m, Allowances = 500m }
        }, false, "tester", "u1");

        var row = Assert.Single(res.Rows);
        Assert.True(row.Saved, row.Error);
        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.SingleAsync(i => i.Id == item.Id);

        Assert.Equal(300m, after.Bonuses);
        Assert.Equal(500m, after.Allowances);
        Assert.Equal(0m, after.OtherDeductions);
        Assert.Equal(9800m, after.GrossEarnings);        // 9000 + 300 + 500
        Assert.Equal(8300m, after.NetSalary);            // 9800 − 1500 غياب
    }

    [Fact]
    public async Task Other_Deduction_Edit_Is_Applied_And_Saved()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        var res = await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, OtherDeductions = 100m }
        }, false, "tester", "u1");
        Assert.True(res.Rows[0].Saved, res.Rows[0].Error);

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(100m, after.OtherDeductions);
        Assert.Equal(1600m, after.TotalDeductions);      // 1500 غياب + 100
        Assert.Equal(7400m, after.NetSalary);
    }

    [Fact]
    public async Task Overtime_Hours_Edit_Uses_Hourly_Rate_Without_Any_Multiplier()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        var res = await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, OvertimeHours = 50m }
        }, false, "tester", "u1");
        var row = Assert.Single(res.Rows);
        Assert.True(row.Saved, row.Error);

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(37.50m, after.HourlyRate);          // 9000 / 30 / 8
        Assert.Equal(50m, after.ApprovedOvertimeHours);
        Assert.Equal(1875m, after.OvertimeAmount);      // 50 × 37.50 (بدون 1.5 أو 2)
        Assert.Equal(1m, after.OvertimeMultiplier);
        Assert.Equal(9375m, after.NetSalary);            // 9000 + 1875 − 1500
    }

    [Fact]
    public async Task Extra_Days_With_Explicit_Rate_Are_Priced_Separately()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        var res = await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ExtraDays = 3m, ExtraDaysRate = 250m }
        }, false, "tester", "u1");
        Assert.True(res.Rows[0].Saved, res.Rows[0].Error);

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(3m, after.AdditionalDaysCount);
        Assert.Equal(250m, after.AdditionalDaysRate);
        Assert.Equal(750m, after.AdditionalDaysAmount);
        Assert.Equal(8250m, after.NetSalary);            // 9000 + 750 − 1500
    }

    // ===== 8: الحفظ واسترجاع القيم =====
    [Fact]
    public async Task Saved_Edits_Are_Returned_With_The_Row_And_Persisted()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 1m, AnnualLeaveDays = 1m, Bonuses = 200m }
        }, false, "tester", "u1");

        var rows = await new ReportService(ctx, new SettingsService(ctx)).GetPayrollRowsAsync(run.Id);
        var row = Assert.Single(rows);
        Assert.Equal(1m, row.AbsenceDays);
        Assert.Equal(5m, row.OriginalAbsenceDays);
        Assert.Equal(1m, row.AnnualLeaveDays);
        Assert.Equal(20m, row.AnnualLeaveBalance);
        Assert.Equal(200m, row.Bonuses);
        Assert.True(row.IsManuallyAdjusted);
        Assert.Equal("tester", row.LastEditedBy);
        Assert.Equal("مُعدَّل يدويًا", row.ReviewStatusText);
    }

    // ===== 9 و10 و11: منع التعديل غير الصحيح أو غير المصرح =====
    [Fact]
    public async Task Editing_An_Approved_Or_Locked_Sheet_Is_Rejected()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        await Payroll(ctx).ApproveAsync(run.Id, "tester", "u1");
        await Payroll(ctx).LockAsync(run.Id, "tester", "u1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
            {
                new() { PayrollItemId = item.Id, Bonuses = 100m }
            }, false, "tester", "u1"));
        Assert.Contains("فتح", ex.Message);

        // إعادة الفتح بسبب مُسجَّل تسمح بالتعديل مرة أخرى
        await Payroll(ctx).ReopenAsync(run.Id, "تصحيح مكافأة", "tester", "u1");
        var res = await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, Bonuses = 100m }
        }, false, "tester", "u1");
        Assert.True(res.Rows[0].Saved, res.Rows[0].Error);
    }

    [Fact]
    public async Task Negative_Values_And_Impossible_Leave_Totals_Are_Rejected()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);
        var sheet = Sheet(ctx);

        var negative = await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = -1m }
        }, false, "tester", "u1");
        Assert.False(negative.Rows[0].Saved);
        Assert.Contains("سالبة", negative.Rows[0].Error!);

        var tooMany = await sheet.SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 20m, AnnualLeaveDays = 10m }
        }, false, "tester", "u1");
        Assert.False(tooMany.Rows[0].Saved);
        Assert.Contains("أيام العمل في الفترة", tooMany.Rows[0].Error!);

        ctx.ChangeTracker.Clear();
        var after = await ctx.PayrollItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(5m, after.AbsenceDays);            // لم يتغير شيء
        Assert.Equal(0m, after.AnnualLeaveDays);
    }

    [Fact]
    public async Task Every_Saved_Edit_Is_Written_To_The_Audit_Log()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 2m, AnnualLeaveDays = 2m, Bonuses = 50m }
        }, false, "أحمد المحاسب", "u9");

        var log = await ctx.AuditLogs
            .Where(a => a.EntityName == nameof(PayrollItem) && a.EntityId == item.Id.ToString())
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(log);
        Assert.Equal("أحمد المحاسب", log!.UserName);
        Assert.Contains("غياب أصلي 5", log.OldValue);       // القيمة الأصلية محفوظة في السجل
        Assert.Contains("سنوية 2", log.NewValue);          // القيمة المعدلة
        Assert.Contains("صافي", log.NewValue);
    }

    [Fact]
    public async Task Regenerating_The_Sheet_Keeps_Manual_Edits_Without_Deducting_The_Balance_Again()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, ApprovedAbsenceDays = 2m, AnnualLeaveDays = 2m, Bonuses = 100m }
        }, false, "tester", "u1");

        // إعادة احتساب نفس الشهر: تُؤرشف النسخة السابقة ويُنشأ إصدار جديد
        var again = await Payroll(ctx).GenerateAsync(2024, 10, "tester", "u1");
        ctx.ChangeTracker.Clear();

        var newItem = await ctx.PayrollItems
            .Include(i => i.Employee)
            .SingleAsync(i => i.PayrollRunId == again.PayrollRunId);

        Assert.Equal(2m, newItem.AbsenceDays);           // التعديل اليدوي نُقل
        Assert.Equal(2m, newItem.AnnualLeaveDays);
        Assert.Equal(100m, newItem.Bonuses);
        Assert.Equal(5m, newItem.OriginalAbsenceDays);   // الغياب الجديد من البصمة
        Assert.True(newItem.IsManuallyAdjusted);
        Assert.Equal(19m, newItem.Employee!.AnnualLeaveBalance);   // لم يُخصم مرتين
    }

    [Fact]
    public async Task Cancelling_The_Sheet_Returns_The_Consumed_Annual_Leave_Balance()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var (run, item) = await GenerateAsync(ctx);

        await Sheet(ctx).SaveAsync(run.Id, new List<PayrollRowEdit>
        {
            new() { PayrollItemId = item.Id, AnnualLeaveDays = 3m }
        }, false, "tester", "u1");

        await Payroll(ctx).CancelAsync(run.Id, "tester", "u1");

        ctx.ChangeTracker.Clear();
        var emp = await ctx.Employees.SingleAsync(e => e.Id == 1);
        Assert.Equal(21m, emp.AnnualLeaveBalance);
    }
}
