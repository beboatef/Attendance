using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>
/// المتطلبات 11 و12: احتساب الرواتب، الإضافي بدون مضاعفة، الخصومات، منع الازدواج،
/// والأيام الإضافية وقفل الفترة.
/// </summary>
public class PayrollTests
{
    private static readonly DateTime PeriodStart = new(2024, 10, 1);

    private static PayrollService Service(AppDbContext ctx)
        => new(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);

    private static async Task<(TestDb Db, AppDbContext Ctx)> SeedAsync(decimal salary = 9000m)
    {
        var db = new TestDb();
        var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(salary: salary));
        await ctx.SaveChangesAsync();
        return (db, ctx);
    }

    /// <summary>ينشئ أيام حضور فعلية داخل الشهر مع إضافي موزّع.</summary>
    private static void AddAttendanceDays(AppDbContext ctx, int days, int totalOvertimeMinutes = 0,
        AttendanceStatus? overrideStatus = null, int count = 0, bool approveOvertime = true)
    {
        var n = count > 0 ? count : days;
        var added = 0;
        // الأيام المُنشأة بالفعل لنفس الموظف في هذا السياق (لتجنب تكرار اليوم نفسه)
        var taken = ctx.DailyAttendances.Local.Where(d => d.EmployeeId == 1)
            .Select(d => d.WorkDate.Date).ToHashSet();
        for (var offset = 0; added < n; offset++)
        {
            var date = PeriodStart.AddDays(offset);
            // أيام العمل فقط: الجمعة يوم راحة أسبوعية مدفوعة
            if (date.DayOfWeek == DayOfWeek.Friday) continue;
            if (!taken.Add(date)) continue;

            // توزيع دقائق الإضافي على الأيام بدون فقدان أي دقيقة (بلا قسمة صحيحة تُنقص الإجمالي)
            var minutesBefore = n == 0 ? 0 : totalOvertimeMinutes * added / n;
            var minutesAfter = n == 0 ? 0 : totalOvertimeMinutes * (added + 1) / n;
            var perDay = minutesAfter - minutesBefore;
            added++;

            var isAbsent = overrideStatus is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence
                           or AttendanceStatus.UnpaidLeave or AttendanceStatus.PaidLeave;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1,
                WorkDate = date,
                ShiftId = 1,
                ShiftStart = date.AddHours(7),
                ShiftEnd = date.AddHours(15),
                CheckInTime = isAbsent ? null : date.AddHours(7),
                CheckOutTime = isAbsent ? null : date.AddHours(15),
                Status = overrideStatus ?? AttendanceStatus.Present,
                RawOvertimeMinutes = isAbsent ? 0 : perDay,
                // الإضافي الخام لا يُعتمد تلقائيًا: الاعتماد يكون صريحًا
                ApprovedOvertimeMinutes = isAbsent || !approveOvertime ? 0 : perDay,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
    }

    /// <summary>ينشئ أيام الراحة الأسبوعية (الجمعة) داخل الشهر.</summary>
    private static void AddRestDays(AppDbContext ctx, int count)
    {
        var added = 0;
        var taken = ctx.DailyAttendances.Local.Where(d => d.EmployeeId == 1)
            .Select(d => d.WorkDate.Date).ToHashSet();
        for (var offset = 0; added < count; offset++)
        {
            var date = PeriodStart.AddDays(offset);
            if (date.DayOfWeek != DayOfWeek.Friday) continue;
            if (!taken.Add(date)) continue;
            added++;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1,
                WorkDate = date,
                ShiftId = 1,
                ShiftStart = date.AddHours(7),
                ShiftEnd = date.AddHours(15),
                Status = AttendanceStatus.RestDay,
                IsWeeklyRestDay = true,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
    }

    // ===================== 27) مثال منى: 9000 راتب + 50 ساعة إضافي + 10 أيام إضافية =====================

    [Fact] // 27 + 24
    public async Task Payroll_Matches_The_Company_Example()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        // 50 ساعة إضافي معتمدة (3000 دقيقة) موزّعة على أيام الشهر
        AddAttendanceDays(ctx, 0, 3000, null, 22);
        ctx.PayrollAdjustments.Add(new PayrollAdjustment
        {
            EmployeeId = 1, Year = 2024, Month = 10, Type = AdjustmentType.AdditionalDays,
            Days = 10, Description = "أيام عمل إضافية خارج فترة الراتب", Status = ApprovalStatus.Approved
        });
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(9000m, item.BasicSalary);
        Assert.Equal(300m, item.DailyRate);          // 9000 / 30
        Assert.Equal(37.50m, item.HourlyRate);       // 300 / 8
        Assert.Equal(50m, item.PaidOvertimeHours);
        Assert.Equal(1875m, item.OvertimeAmount);    // 50 × 37.50 بدون مضاعف
        Assert.Equal(10m, item.AdditionalDaysCount);
        Assert.Equal(3000m, item.AdditionalDaysAmount); // 10 × 300
        Assert.Equal(13875m, item.GrossEarnings);    // 9000 + 1875 + 3000
        Assert.Equal(0m, item.TotalDeductions);
        Assert.Equal(13875m, item.NetSalary);
        Assert.Equal(22, item.AttendanceDays);
        Assert.NotNull(item.CalculationDetails);
        Assert.Contains("overtimeMultiplier", item.CalculationDetails);
    }

    [Fact]
    public async Task Paid_Rest_Days_Are_Reported_But_Not_Paid_Twice()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        AddRestDays(ctx, 4);   // أيام الجمعة داخل الشهر
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(4, item.PaidRestDays);
        Assert.Equal(9000m, item.GrossEarnings);  // لم تُضف قيمة أيام الراحة مرة أخرى
        Assert.Equal(9000m, item.NetSalary);
    }

    // ===================== 12) اعتماد ورفض الإضافي =====================

    [Fact] // 12
    public async Task Unapproved_Overtime_Is_Not_Paid_And_Approved_Overtime_Is_Paid()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        // إضافي خام بدون اعتماد: 50 ساعة
        AddAttendanceDays(ctx, 0, 3000, null, 22, approveOvertime: false);
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var first = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item1 = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == first.PayrollRunId);

        Assert.Equal(50m, item1.RawOvertimeHours);
        Assert.Equal(0m, item1.ApprovedOvertimeHours);
        Assert.Equal(0m, item1.OvertimeAmount);       // لم يُعتمد ⇒ لا يُصرف
        Assert.Equal(9000m, item1.NetSalary);

        // اعتماد 10 ساعات فقط (600 دقيقة موزعة)
        foreach (var d in ctx.DailyAttendances.ToList()) d.ApprovedOvertimeMinutes = 0;
        var approved = await ctx.DailyAttendances.OrderBy(d => d.WorkDate).Take(10).ToListAsync();
        foreach (var d in approved) d.ApprovedOvertimeMinutes = 60;   // 10 × 60 = 600 دقيقة
        await ctx.SaveChangesAsync();

        var second = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item2 = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == second.PayrollRunId);

        Assert.Equal(10m, item2.ApprovedOvertimeHours);
        Assert.Equal(375m, item2.OvertimeAmount);     // 10 ساعات × 37.50 بدون مضاعف
        Assert.Equal(9375m, item2.NetSalary);
    }

    [Fact]
    public async Task Day_Level_Approval_Drives_Payment_And_Is_Not_Double_Counted_With_Period_Approval()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 600, null, 10);      // إضافي خام 600 دقيقة
        await ctx.SaveChangesAsync();

        // اعتماد على مستوى اليوم: 600 دقيقة
        foreach (var d in ctx.DailyAttendances.ToList()) d.ApprovedOvertimeMinutes = 60;
        // واعتماد على مستوى الفترة لنفس الساعات (يجب ألا يُضاف مرة أخرى)
        ctx.OvertimeApprovals.Add(new OvertimeApproval
        {
            EmployeeId = 1, PeriodStart = PeriodStart, PeriodEnd = PeriodStart.AddMonths(1).AddDays(-1),
            CalculatedHours = 10m, ApprovedHours = 10m, OvertimeMultiplier = 1m, Status = ApprovalStatus.Approved
        });
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(10m, item.PaidOvertimeHours);   // لا 20 ساعة
        Assert.Equal(375m, item.OvertimeAmount);
    }

    [Fact]
    public async Task Period_Level_Approval_Is_Used_When_No_Day_Level_Approval_Exists()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 600, null, 10, approveOvertime: false);
        ctx.OvertimeApprovals.Add(new OvertimeApproval
        {
            EmployeeId = 1, PeriodStart = PeriodStart, PeriodEnd = PeriodStart.AddMonths(1).AddDays(-1),
            CalculatedHours = 10m, ApprovedHours = 8m, OvertimeMultiplier = 1m, Status = ApprovalStatus.Approved
        });
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(8m, item.ApprovedOvertimeHours);
        Assert.Equal(300m, item.OvertimeAmount);     // 8 × 37.50
    }

    // ===================== 21) خصم الإجازة بدون أجر =====================

    [Fact] // 21
    public async Task Unpaid_Leave_Is_Deducted_At_The_Daily_Rate()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        AddAttendanceDays(ctx, 0, 0, AttendanceStatus.UnpaidLeave, 2);
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(2m, item.UnpaidLeaveDays);
        Assert.Equal(600m, item.UnpaidLeaveDeduction);   // 2 × 300
        Assert.Equal(9000m, item.GrossEarnings);
        Assert.Equal(8400m, item.NetSalary);
    }

    // ===================== 22) خصم الغياب =====================

    [Fact] // 22
    public async Task Absence_Is_Deducted_At_The_Daily_Rate()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        AddAttendanceDays(ctx, 0, 0, AttendanceStatus.Absent, 3);
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(3m, item.AbsenceDays);
        Assert.Equal(900m, item.AbsenceDeduction);       // 3 × 300
        Assert.Equal(8100m, item.NetSalary);
    }

    [Fact]
    public async Task Late_Deduction_Uses_The_Minute_Rate()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        await ctx.SaveChangesAsync();
        var first = await ctx.DailyAttendances.OrderBy(d => d.WorkDate).FirstAsync();
        first.LateMinutes = 120;
        first.Status = AttendanceStatus.Late;
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(120, item.LateMinutes);
        Assert.Equal(75m, item.LateDeduction);   // 120 × 0.625 = 75
        Assert.Equal(8925m, item.NetSalary);
    }

    [Fact]
    public async Task Approved_Annual_Leave_Is_Paid_And_Not_Deducted()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        AddAttendanceDays(ctx, 0, 0, AttendanceStatus.PaidLeave, 3);
        ctx.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = 1, LeaveType = LeaveType.Annual, StartDate = PeriodStart, EndDate = PeriodStart.AddDays(2),
            Status = ApprovalStatus.Approved, IsPaid = true, DaysCount = 3, WorkingDays = 3
        });
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(3m, item.PaidLeaveDays);
        Assert.Equal(0m, item.UnpaidLeaveDeduction);
        Assert.Equal(9000m, item.NetSalary);
    }

    // ===================== 23) منع تكرار الخصم =====================

    [Fact] // 23
    public async Task Loans_And_Deductions_Are_Applied_Once_Even_After_Recalculation()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        ctx.Loans.Add(new Loan { EmployeeId = 1, Amount = 500m, LoanDate = PeriodStart, DeductionMonth = 10, DeductionYear = 2024, Status = ApprovalStatus.Approved });
        ctx.Deductions.Add(new Deduction { EmployeeId = 1, Amount = 100m, Reason = "جزاء", DeductionDate = PeriodStart, Month = 10, Year = 2024, Status = ApprovalStatus.Approved });
        ctx.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = 1, Year = 2024, Month = 10, Type = AdjustmentType.OtherDeduction, Amount = 50m, Description = "خصم آخر", Status = ApprovalStatus.Approved });
        ctx.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = 1, Year = 2024, Month = 10, Type = AdjustmentType.Bonus, Amount = 200m, Description = "مكافأة", Status = ApprovalStatus.Approved });
        ctx.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = 1, Year = 2024, Month = 10, Type = AdjustmentType.Allowance, Amount = 300m, Description = "بدل", Status = ApprovalStatus.Approved });
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var r1 = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item1 = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == r1.PayrollRunId);

        Assert.Equal(500m, item1.LoansTotal);
        Assert.Equal(100m, item1.DeductionsTotal);
        Assert.Equal(50m, item1.OtherDeductions);
        Assert.Equal(200m, item1.Bonuses);
        Assert.Equal(300m, item1.Allowances);
        Assert.Equal(9500m, item1.GrossEarnings);                // 9000 + 200 + 300
        Assert.Equal(650m, item1.TotalDeductions);               // 500 + 100 + 50
        Assert.Equal(8850m, item1.NetSalary);

        // إعادة الاحتساب: يجب أن تظل كل قيمة مرة واحدة فقط
        var r2 = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item2 = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == r2.PayrollRunId);

        Assert.Equal(500m, item2.LoansTotal);
        Assert.Equal(100m, item2.DeductionsTotal);
        Assert.Equal(8850m, item2.NetSalary);
        Assert.Equal(1, await ctx.Loans.CountAsync(l => l.PayrollItemId != null));
    }

    // ===================== 25) منع الدفع المزدوج بين الفترات =====================

    [Fact] // 25
    public async Task Components_Already_Paid_In_A_Previous_Period_Are_Not_Paid_Again()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);   // أكتوبر
        ctx.Loans.Add(new Loan { EmployeeId = 1, Amount = 500m, LoanDate = PeriodStart, DeductionMonth = 10, DeductionYear = 2024, Status = ApprovalStatus.Approved });
        ctx.PayrollAdjustments.Add(new PayrollAdjustment
        {
            EmployeeId = 1, Year = 2024, Month = 10, Type = AdjustmentType.AdditionalDays, Days = 10,
            Description = "أيام إضافية أكتوبر", Status = ApprovalStatus.Approved
        });
        // أيام نوفمبر (20 يوم عمل، مع تجاهل الجمعة)
        var novAdded = 0;
        for (var offset = 0; novAdded < 20; offset++)
        {
            var date = new DateTime(2024, 11, 1).AddDays(offset);
            if (date.DayOfWeek == DayOfWeek.Friday) continue;
            novAdded++;
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1, WorkDate = date, ShiftId = 1, ShiftStart = date.AddHours(7), ShiftEnd = date.AddHours(15),
                CheckInTime = date.AddHours(7), CheckOutTime = date.AddHours(15), Status = AttendanceStatus.Present,
                PunchReviewStatus = ClassificationStatus.Classified
            });
        }
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var oct = await service.GenerateAsync(2024, 10, "tester", "u1");
        var octItem = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == oct.PayrollRunId);
        Assert.Equal(500m, octItem.LoansTotal);
        Assert.Equal(3000m, octItem.AdditionalDaysAmount);

        var nov = await service.GenerateAsync(2024, 11, "tester", "u1");
        var novItem = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == nov.PayrollRunId);

        Assert.Equal(0m, novItem.LoansTotal);              // السلفة خُصمت في أكتوبر
        Assert.Equal(0m, novItem.AdditionalDaysAmount);    // الأيام الإضافية دُفعت في أكتوبر
        Assert.Equal(9000m, novItem.NetSalary);
    }

    // ===================== 26) منع التغيير الصامت على كشف مقفل =====================

    [Fact] // 26
    public async Task Locked_Payroll_Cannot_Be_Recalculated_Without_An_Explicit_Reopen()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var run = await service.GenerateAsync(2024, 10, "tester", "u1");
        await service.ApproveAsync(run.PayrollRunId, "tester", "u1");
        await service.LockAsync(run.PayrollRunId, "tester", "u1");

        var persisted = await ctx.PayrollRuns.SingleAsync(r => r.Id == run.PayrollRunId);
        Assert.Equal(PayrollStatus.Locked, persisted.Status);

        // محاولة إعادة الاحتساب بدون إعادة فتح ⇒ تفشل صراحةً
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(2024, 10, "tester", "u1"));
        Assert.Contains("مقفل", ex.Message);

        // إعادة الفتح بسبب مُسجَّل تسمح بالاحتساب
        await service.ReopenAsync(run.PayrollRunId, "تصحيح بصمة خروج مفقودة", "tester", "u1");
        var reopened = await ctx.PayrollRuns.SingleAsync(r => r.Id == run.PayrollRunId);
        Assert.Equal(PayrollStatus.Reopened, reopened.Status);
        Assert.Equal("تصحيح بصمة خروج مفقودة", reopened.ReopenReason);

        var second = await service.GenerateAsync(2024, 10, "tester", "u1");
        Assert.True(second.Version > run.Version);
    }

    [Fact]
    public async Task Reopen_Requires_A_Reason()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;
        AddAttendanceDays(ctx, 0, 0, null, 20);
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var run = await service.GenerateAsync(2024, 10, "tester", "u1");
        await service.ApproveAsync(run.PayrollRunId, "tester", "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReopenAsync(run.PayrollRunId, "", "tester", "u1"));
    }

    [Fact]
    public async Task Locking_Requires_Approval_First()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;
        AddAttendanceDays(ctx, 0, 0, null, 20);
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var run = await service.GenerateAsync(2024, 10, "tester", "u1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LockAsync(run.PayrollRunId, "tester", "u1"));
    }

    [Fact]
    public async Task Attendance_Correction_Is_Blocked_For_A_Locked_Payroll_Period()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 0, null, 20);
        await ctx.SaveChangesAsync();

        var payroll = Service(ctx);
        var run = await payroll.GenerateAsync(2024, 10, "tester", "u1");
        await payroll.ApproveAsync(run.PayrollRunId, "tester", "u1");

        var daily = await ctx.DailyAttendances.OrderBy(d => d.WorkDate).FirstAsync();
        var corrections = new AttendanceCorrectionService(ctx, new AuditService(ctx));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => corrections.CorrectAsync(
            new AttendanceCorrectionService.CorrectionRequest
            {
                DailyAttendanceId = daily.Id,
                CheckOut = daily.WorkDate.AddHours(16),
                Reason = "تعديل بعد الاعتماد"
            }, "tester", "u1"));

        Assert.Contains("مقفلة", ex.Message);
    }

    [Fact]
    public async Task Payroll_Run_Totals_Match_The_Items()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 600, null, 20);
        await ctx.SaveChangesAsync();
        foreach (var d in await ctx.DailyAttendances.ToListAsync()) d.ApprovedOvertimeMinutes = 30;

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var run = await ctx.PayrollRuns.SingleAsync(r => r.Id == result.PayrollRunId);
        var items = await ctx.PayrollItems.Where(i => i.PayrollRunId == run.Id).ToListAsync();

        Assert.Equal(items.Sum(i => i.NetSalary), run.TotalNet);
        Assert.Equal(items.Sum(i => i.GrossEarnings), run.TotalGross);
        Assert.Equal(items.Sum(i => i.OvertimeAmount), run.TotalOvertime);
        Assert.Equal(items.Sum(i => i.TotalDeductions), run.TotalDeductions);
    }

    [Fact]
    public async Task Inactive_Employees_Are_Excluded_But_Terminated_Mid_Period_Are_Included()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        ctx.Employees.Add(new Employee
        {
            Id = 2, EmployeeCode = "2002", FullName = "منتهي الخدمة", BasicSalary = 5000m,
            HireDate = new DateTime(2020, 1, 1), EmploymentStatus = EmploymentStatus.Terminated,
            IsActive = false, TerminationDate = new DateTime(2024, 5, 1)
        });
        ctx.Employees.Add(new Employee
        {
            Id = 3, EmployeeCode = "2003", FullName = "انتهى خلال الفترة", BasicSalary = 6000m,
            HireDate = new DateTime(2020, 1, 1), EmploymentStatus = EmploymentStatus.Terminated,
            IsActive = false, TerminationDate = new DateTime(2024, 10, 15)
        });
        AddAttendanceDays(ctx, 0, 0, null, 20);
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var items = await ctx.PayrollItems.Where(i => i.PayrollRunId == result.PayrollRunId).ToListAsync();

        Assert.Contains(items, i => i.EmployeeId == 1);
        Assert.Contains(items, i => i.EmployeeId == 3);      // انتهى داخل الفترة ⇒ يُحتسب
        Assert.DoesNotContain(items, i => i.EmployeeId == 2); // منتهي قبل الفترة ⇒ لا يُحتسب
    }

    // ===================== 21) تطابق قيمة الإضافي بين شاشة الاعتماد وكشف الراتب =====================

    [Theory] // 21 + 10 + 11
    [InlineData(40, 37.50, 25.13)]
    [InlineData(60, 37.50, 37.50)]
    [InlineData(100, 37.50, 62.63)]
    [InlineData(120, 37.50, 75.00)]
    [InlineData(1, 37.50, 0.75)]
    [InlineData(3000, 37.50, 1875.00)]
    public void Overtime_Minutes_Are_Converted_To_Hours_With_One_Rounding_Rule(
        int minutes, decimal hourlyRate, decimal expected)
    {
        // نفس الدالة يستخدمها كشف الراتب وشاشة اعتماد الإضافي.
        // القاعدة: تجميع دقائق الفترة كلها ثم تحويلها إلى ساعات مرة واحدة (منزلتان)
        // قبل ضربها في الأجر بالساعة — أي أن التقريب يحدث مرة واحدة على الإجمالي.
        Assert.Equal(expected, PayrollCalculator.OvertimeValueForMinutes(minutes, hourlyRate));
    }

    [Fact]
    public async Task Approval_Screen_Value_Equals_The_Payroll_Overtime_Amount()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        // 40 دقيقة إضافي معتمدة على يوم واحد
        AddAttendanceDays(ctx, 0, 40, null, 1);
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        var policy = await new SettingsService(ctx).GetPolicyAsync();
        var screenValue = PayrollCalculator.OvertimeValueForMinutes(40, item.HourlyRate);
        var screenValue2 = PayrollCalculator.CalculateOvertimeValueFromMinutes(9000m, 40, 1m, policy);

        Assert.Equal(item.OvertimeAmount, screenValue);
        Assert.Equal(item.OvertimeAmount, screenValue2);
    }

    [Fact]
    public async Task Overtime_Hours_Are_Not_Double_Counted_When_Period_Is_Recalculated()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        AddAttendanceDays(ctx, 0, 300, null, 5);   // 5 ساعات إضافي معتمدة
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var first = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item1 = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == first.PayrollRunId);
        Assert.Equal(5m, item1.PaidOvertimeHours);
        Assert.Equal(187.50m, item1.OvertimeAmount);

        var second = await service.GenerateAsync(2024, 10, "tester", "u1");
        var items = await ctx.PayrollItems.Where(i => i.PayrollRunId == second.PayrollRunId).ToListAsync();
        var item2 = Assert.Single(items);
        Assert.Equal(5m, item2.PaidOvertimeHours);
        Assert.Equal(187.50m, item2.OvertimeAmount);
    }

    // ===================== 11.5) لا يُحتسب كشف بلا بيانات حضور =====================

    [Fact]
    public async Task Payroll_Is_Refused_When_The_Period_Has_No_Attendance_At_All()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(ctx).GenerateAsync(2024, 10, "tester", "u1"));

        Assert.Contains("لا توجد أي بيانات حضور معالجة", ex.Message);
        Assert.Empty(await ctx.PayrollRuns.ToListAsync());   // لم يُنشأ أي كشف
    }

    [Fact]
    public async Task Payroll_Can_Be_Generated_Without_Attendance_When_The_Admin_Allows_It()
    {
        var (db, ctx) = await SeedAsync(9000m);
        using var _ = db;

        await new SettingsService(ctx).SetAsync(
            SettingKeys.AllowPayrollWithoutAttendance, "true", null, "tester");

        var result = await Service(ctx).GenerateAsync(2024, 10, "tester", "u1");
        var item = await ctx.PayrollItems.SingleAsync(i => i.PayrollRunId == result.PayrollRunId);

        Assert.Equal(9000m, item.NetSalary);
    }
}
