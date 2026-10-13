using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>المتطلبات 6.3 و9 و10: معالجة الحضور، أيام الراحة، العطلات، والإجازات.</summary>
public class AttendanceProcessingTests
{
    private static AttendanceProcessingService Service(AppDbContext ctx)
        => new(ctx, new SettingsService(ctx), NullLogger<AttendanceProcessingService>.Instance);

    private static async Task<AppDbContext> SeedAsync(TestDb db, params Shift[] shifts)
    {
        var ctx = db.CreateContext();
        ctx.Shifts.AddRange(shifts.Length > 0 ? shifts : new[] { TestDb.FirstShift() });
        ctx.Employees.Add(TestDb.Employee(salary: 9000m));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static RawPunch AddPunch(AppDbContext ctx, DateTime time, string code = "1001", string? type = null)
        => new()
        {
            DeviceEmployeeCode = code,
            DeviceId = "1",
            AttType = type,
            PunchTime = time,
            RawPunchTimeText = time.ToString("yyyy-MM-dd HH:mm:ss"),
            DedupHash = AttendanceImportService.ComputeHash("1", code, time, type),
            EmployeeId = 1,
            ImportBatchId = null
        };

    // ===================== 19) الجمعة يوم راحة مدفوعة =====================

    [Fact] // 19
    public async Task Friday_Is_A_Paid_Weekly_Rest_Day_And_Not_Absence()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        // بيانات مستوردة تغطي الجمعة (بصمة دخول وخروج في يوم آخر لإنشاء نطاق البيانات)
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(7)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(15)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Friday.AddHours(7)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Friday.AddHours(15)));
        await ctx.SaveChangesAsync();

        var result = await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Friday);

        var friday = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Friday);
        Assert.True(friday.IsWeeklyRestDay);
        Assert.Equal(AttendanceStatus.Present, friday.Status);   // حضر فعلًا
        Assert.NotNull(friday.NeedsReviewReason);
        Assert.Contains("راحة", friday.NeedsReviewReason!);
        Assert.True(result.RestDays > 0 || result.ReviewCases > 0);
    }

    [Fact]
    public async Task Friday_Without_Punches_Is_Rest_Day_Not_Absence()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(7)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(15)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Friday.AddHours(9)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Friday.AddHours(17)));
        await ctx.SaveChangesAsync();

        // معالجة يوم جمعة فقط بعد أن أُخليت بصماته
        foreach (var p in ctx.RawPunches.Where(p => p.PunchTime.Date == TestDb.Friday)) p.EmployeeId = null;
        await ctx.SaveChangesAsync();

        await Service(ctx).ProcessAsync(TestDb.Friday, TestDb.Friday);

        var friday = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Friday);
        Assert.Equal(AttendanceStatus.RestDay, friday.Status);
        Assert.True(friday.IsWeeklyRestDay);
        Assert.NotEqual(AttendanceStatus.Absent, friday.Status);
    }

    // ===================== 20) الإجازة السنوية المعتمدة =====================

    [Fact] // 20
    public async Task Approved_Annual_Leave_Is_Paid_Leave_Not_Absence()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        ctx.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = 1,
            LeaveType = LeaveType.Annual,
            StartDate = TestDb.Monday,
            EndDate = TestDb.Monday,
            Status = ApprovalStatus.Approved,
            IsPaid = true,
            DaysCount = 1,
            WorkingDays = 1
        });
        // بصمات في يوم آخر لإنشاء نطاق بيانات مستورد
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddDays(2).AddHours(7)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddDays(2).AddHours(15)));
        await ctx.SaveChangesAsync();

        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var monday = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        Assert.Equal(AttendanceStatus.PaidLeave, monday.Status);
        Assert.NotNull(monday.LeaveRequestId);
        Assert.NotEqual(AttendanceStatus.Absent, monday.Status);
    }

    [Fact]
    public async Task Unpaid_Leave_Is_Classified_As_Unpaid()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        ctx.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = 1,
            LeaveType = LeaveType.Unpaid,
            StartDate = TestDb.Monday,
            EndDate = TestDb.Monday,
            Status = ApprovalStatus.Approved,
            IsPaid = false,
            DaysCount = 1,
            WorkingDays = 1
        });
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddDays(2).AddHours(7)));
        await ctx.SaveChangesAsync();

        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var monday = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        Assert.Equal(AttendanceStatus.UnpaidLeave, monday.Status);
    }

    // ===================== العطلات الرسمية =====================

    [Fact]
    public async Task Public_Holiday_Is_Not_Counted_As_Absence()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        ctx.PublicHolidays.Add(new PublicHoliday { Name = "عيد", HolidayDate = TestDb.Monday, IsPaid = true, IsActive = true });
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddDays(2).AddHours(7)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddDays(2).AddHours(15)));
        await ctx.SaveChangesAsync();

        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var monday = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        Assert.Equal(AttendanceStatus.Holiday, monday.Status);
        Assert.True(monday.IsHoliday);
        Assert.NotEqual(AttendanceStatus.Absent, monday.Status);
    }

    // ===================== 18) بيانات جديدة ليوم مستورد مسبقًا =====================

    [Fact] // 18
    public async Task Reprocessing_Picks_Up_New_Punches_For_An_Already_Imported_Day()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        // اليوم الأول: بصمة دخول فقط (خروج مفقود)
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(7).AddMinutes(35)));
        await ctx.SaveChangesAsync();

        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);
        var first = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        Assert.Equal(AttendanceStatus.MissingCheckOut, first.Status);

        // استيراد لاحق يضيف بصمة الخروج لنفس اليوم بدون فقدان السجل السابق
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(16)));
        await ctx.SaveChangesAsync();

        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var updated = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        Assert.Equal(AttendanceStatus.Late, updated.Status);
        Assert.Equal(35, updated.LateMinutes);
        Assert.Equal(60, updated.RawOvertimeMinutes);
        Assert.False(updated.HasIncompletePunches);
        Assert.Equal(1, await ctx.DailyAttendances.CountAsync()); // لم يُنشأ سجل مكرر
    }

    // ===================== 6.4) التصحيح اليدوي =====================

    [Fact]
    public async Task Manual_Correction_Records_Original_Reason_And_User()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(7).AddMinutes(35)));
        await ctx.SaveChangesAsync();
        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var daily = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        var service = new AttendanceCorrectionService(ctx, new AuditService(ctx));

        await service.CorrectAsync(new AttendanceCorrectionService.CorrectionRequest
        {
            DailyAttendanceId = daily.Id,
            CheckOut = TestDb.Monday.AddHours(15).AddMinutes(10),
            ApprovedOvertimeMinutes = 0,
            Reason = "نسي الموظف تسجيل الخروج وتم التصحيح بناءً على تقرير المشرف",
            Approve = true
        }, "tester", "u1");

        var updated = await ctx.DailyAttendances.SingleAsync(d => d.Id == daily.Id);
        Assert.Equal(TestDb.Monday.AddHours(15).AddMinutes(10), updated.CheckOutTime);
        Assert.True(updated.IsManualOverride);

        var correction = await ctx.AttendanceCorrections.SingleAsync();
        Assert.Equal("CheckOutTime", correction.FieldName);
        Assert.Null(correction.OldValue);
        Assert.Contains("15:10", correction.NewValue);
        Assert.Equal("tester", correction.CreatedBy);
        Assert.True(correction.Status == ApprovalStatus.Approved);
    }

    [Fact]
    public async Task Reprocessing_Preserves_The_Approved_Manual_Correction()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);

        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(7).AddMinutes(35)));
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(16)));
        await ctx.SaveChangesAsync();
        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var daily = await ctx.DailyAttendances.SingleAsync(d => d.WorkDate == TestDb.Monday);
        var rawCount = await ctx.RawPunches.CountAsync();

        var corrections = new AttendanceCorrectionService(ctx, new AuditService(ctx));
        await corrections.CorrectAsync(new AttendanceCorrectionService.CorrectionRequest
        {
            DailyAttendanceId = daily.Id,
            CheckOut = TestDb.Monday.AddHours(15).AddMinutes(20),
            Reason = "تأكيد وقت الخروج الفعلي بعد مراجعة المشرف"
        }, "tester", "u1");

        // إعادة معالجة نفس اليوم: يجب ألا تُستبدل القيمة المصحّحة بالبصمة الخام
        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var updated = await ctx.DailyAttendances.SingleAsync(d => d.Id == daily.Id);
        Assert.Equal(TestDb.Monday.AddHours(15).AddMinutes(20), updated.CheckOutTime);
        Assert.True(updated.IsManualOverride);
        Assert.Contains("تصحيح يدوي", updated.NeedsReviewReason!);
        Assert.Equal(rawCount, await ctx.RawPunches.CountAsync());              // البصمات الخام محفوظة
        Assert.Equal(1, await ctx.AttendanceCorrections.CountAsync());          // سجل التصحيح محفوظ
    }

    [Fact]
    public async Task Correction_Without_Reason_Is_Rejected()
    {
        using var db = new TestDb();
        await using var ctx = await SeedAsync(db);
        ctx.RawPunches.Add(AddPunch(ctx, TestDb.Monday.AddHours(7)));
        await ctx.SaveChangesAsync();
        await Service(ctx).ProcessAsync(TestDb.Monday, TestDb.Monday);

        var daily = await ctx.DailyAttendances.SingleAsync();
        var service = new AttendanceCorrectionService(ctx, new AuditService(ctx));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectAsync(
            new AttendanceCorrectionService.CorrectionRequest
            {
                DailyAttendanceId = daily.Id,
                CheckOut = TestDb.Monday.AddHours(15),
                Reason = ""
            }, "tester", "u1"));
    }
}
