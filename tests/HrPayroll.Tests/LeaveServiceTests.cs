using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>المتطلب 10: حساب أيام عمل الإجازة، منع التداخل، وتحويل الغياب إلى إجازة معتمدة.</summary>
public class LeaveServiceTests
{
    private static LeaveService Service(Infrastructure.Persistence.AppDbContext ctx)
        => new(ctx, new SettingsService(ctx), new AuditService(ctx));

    private static async Task<(TestDb Db, Infrastructure.Persistence.AppDbContext Ctx)> SeedAsync()
    {
        var db = new TestDb();
        var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(salary: 9000m));
        await ctx.SaveChangesAsync();
        return (db, ctx);
    }

    [Fact]
    public async Task Working_Days_Exclude_Fridays()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var service = Service(ctx);

        // أسبوع كامل من الأحد إلى السبت يحتوي جمعة واحدة
        var days = await service.CountWorkingDaysAsync(TestDb.Sunday, TestDb.Sunday.AddDays(6));
        Assert.Equal(6m, days);
    }

    [Fact]
    public async Task Working_Days_Exclude_Public_Holidays()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        ctx.PublicHolidays.Add(new PublicHoliday { Name = "عيد", HolidayDate = TestDb.Monday, IsActive = true });
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        var days = await service.CountWorkingDaysAsync(TestDb.Monday, TestDb.Monday);
        Assert.Equal(0m, days);
    }

    [Fact]
    public async Task Overlapping_Leaves_Are_Rejected()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var service = Service(ctx);

        await service.CreateAsync(new LeaveRequest
        {
            EmployeeId = 1, LeaveType = LeaveType.Annual,
            StartDate = TestDb.Monday, EndDate = TestDb.Monday.AddDays(3), IsPaid = true
        }, "tester");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new LeaveRequest
        {
            EmployeeId = 1, LeaveType = LeaveType.Annual,
            StartDate = TestDb.Monday.AddDays(2), EndDate = TestDb.Monday.AddDays(5), IsPaid = true
        }, "tester"));

        Assert.Contains("تداخل", ex.Message);
    }

    [Fact]
    public async Task Approving_Annual_Leave_Deducts_The_Balance_Only_Once()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var service = Service(ctx);

        var employee = await ctx.Employees.FirstAsync();
        employee.AnnualLeaveEntitlement = 21m;
        employee.AnnualLeaveBalance = 21m;
        await ctx.SaveChangesAsync();

        var leave = await service.CreateAsync(new LeaveRequest
        {
            EmployeeId = 1, LeaveType = LeaveType.Annual,
            StartDate = TestDb.Sunday, EndDate = TestDb.Sunday.AddDays(2), IsPaid = true
        }, "tester");

        Assert.Equal(3m, leave.WorkingDays);

        await service.ApproveAsync(leave.Id, "tester");
        var afterFirst = await ctx.Employees.FirstAsync();
        Assert.Equal(18m, afterFirst.AnnualLeaveBalance);

        // الاعتماد مرة أخرى لا يخصم الرصيد مجددًا
        await service.ApproveAsync(leave.Id, "tester");
        var afterSecond = await ctx.Employees.FirstAsync();
        Assert.Equal(18m, afterSecond.AnnualLeaveBalance);
    }

    [Fact]
    public async Task Rejecting_An_Approved_Annual_Leave_Restores_The_Balance()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;
        var service = Service(ctx);

        var employee = await ctx.Employees.FirstAsync();
        employee.AnnualLeaveBalance = 21m;
        await ctx.SaveChangesAsync();

        var leave = await service.CreateAsync(new LeaveRequest
        {
            EmployeeId = 1, LeaveType = LeaveType.Annual,
            StartDate = TestDb.Monday, EndDate = TestDb.Monday.AddDays(1), IsPaid = true
        }, "tester");

        await service.ApproveAsync(leave.Id, "tester");
        Assert.Equal(19m, (await ctx.Employees.FirstAsync()).AnnualLeaveBalance);

        await service.RejectAsync(leave.Id, "tester");
        Assert.Equal(21m, (await ctx.Employees.FirstAsync()).AnnualLeaveBalance);
    }

    [Fact]
    public async Task Converting_Absence_Changes_Only_The_Selected_Days()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;

        // أربعة أيام غياب
        var dates = new[] { TestDb.Sunday, TestDb.Monday, TestDb.Sunday.AddDays(2), TestDb.Sunday.AddDays(3) };
        foreach (var d in dates)
        {
            ctx.DailyAttendances.Add(new DailyAttendance
            {
                EmployeeId = 1, WorkDate = d, ShiftId = 1, ShiftStart = d.AddHours(7), ShiftEnd = d.AddHours(15),
                Status = AttendanceStatus.Absent, PunchReviewStatus = ClassificationStatus.Classified
            });
        }
        await ctx.SaveChangesAsync();

        var service = Service(ctx);
        // اعتماد يومين فقط كإجازة سنوية
        var leave = await service.ConvertAbsenceAsync(1, dates.Take(2), LeaveType.Annual, "اعتماد سنوية", "tester");

        Assert.Equal(2, leave.WorkingDays);
        Assert.True(leave.CreatedFromAbsence);

        var all = await ctx.DailyAttendances.OrderBy(d => d.WorkDate).ToListAsync();
        Assert.Equal(2, all.Count(d => d.Status == AttendanceStatus.PaidLeave));
        Assert.Equal(2, all.Count(d => d.Status == AttendanceStatus.Absent));   // الباقي غياب
        Assert.Equal(2, await ctx.AttendanceCorrections.CountAsync());          // وأثر تدقيق لكل تغيير
    }

    [Fact]
    public async Task Converting_To_Unpaid_Leave_Marks_The_Days_As_Unpaid()
    {
        var (db, ctx) = await SeedAsync();
        using var _ = db;

        ctx.DailyAttendances.Add(new DailyAttendance
        {
            EmployeeId = 1, WorkDate = TestDb.Monday, ShiftId = 1, ShiftStart = TestDb.Monday.AddHours(7),
            ShiftEnd = TestDb.Monday.AddHours(15), Status = AttendanceStatus.Absent,
            PunchReviewStatus = ClassificationStatus.Classified
        });
        await ctx.SaveChangesAsync();

        await Service(ctx).ConvertAbsenceAsync(1, new[] { TestDb.Monday }, LeaveType.Unpaid, "بدون رصيد", "tester");

        var daily = await ctx.DailyAttendances.SingleAsync();
        Assert.Equal(AttendanceStatus.UnpaidLeave, daily.Status);
    }
}
