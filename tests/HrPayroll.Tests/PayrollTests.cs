using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using HrPayroll.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>الاختبار 12: السلف والخصومات لا تُخصم مرتين + معادلة صافي الراتب.</summary>
public class PayrollTests
{
    [Fact]
    public void Net_Salary_Equation_Is_Correct()
    {
        var policy = new AttendancePolicy(); // 30 يوم × 8 ساعات

        var result = PayrollCalculator.Compute(
            basicSalary: 4800m,
            lateMinutes: 75,
            approvedOvertimeHours: 2m,
            overtimeMultiplier: 1.5m,
            loansTotal: 500m,
            deductionsTotal: 100m,
            otherDeductions: 0m,
            policy: policy);

        Assert.Equal(20m, policy.HourlyRate(4800m));          // 4800 / 240
        Assert.Equal(25.00m, result.LateDeduction);            // 75 * (4800/14400)
        Assert.Equal(60.00m, result.OvertimeValue);            // 2 * 20 * 1.5
        Assert.Equal(4860.00m, result.GrossSalary);            // 4800 + 60
        Assert.Equal(4235.00m, result.NetSalary);              // 4800 + 60 - 25 - 500 - 100
    }

    [Fact]
    public void Late_Deduction_Is_Disabled_When_Policy_Says_So()
    {
        var policy = new AttendancePolicy { LateDeductionEnabled = false };
        var result = PayrollCalculator.Compute(4800m, 120, 0, 1, 0, 0, 0, policy);
        Assert.Equal(0m, result.LateDeduction);
        Assert.Equal(4800m, result.NetSalary);
    }

    [Fact]
    public async Task Loan_And_Deduction_Are_Applied_Once_And_Not_Duplicated()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();

        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(salary: 4800m));
        ctx.Loans.Add(new Loan { EmployeeId = 1, Amount = 500m, LoanDate = new DateTime(2024, 10, 1), DeductionMonth = 10, DeductionYear = 2024, Status = ApprovalStatus.Approved });
        ctx.Deductions.Add(new Deduction { EmployeeId = 1, Amount = 100m, Reason = "جزاء", DeductionDate = new DateTime(2024, 10, 2), Month = 10, Year = 2024, Status = ApprovalStatus.Approved });
        await ctx.SaveChangesAsync();

        var service = new PayrollService(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);

        // الإصدار الأول
        var r1 = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item1 = ctx.PayrollItems.Single(i => i.PayrollRunId == r1.PayrollRunId);
        Assert.Equal(500m, item1.LoansTotal);
        Assert.Equal(100m, item1.DeductionsTotal);
        Assert.Equal(4200m, item1.NetSalary); // 4800 - 500 - 100
        Assert.NotNull(ctx.Loans.Single().PayrollItemId);

        // إعادة التوليد لنفس الشهر: يجب أن تظل السلفة والخصم مرة واحدة فقط
        var r2 = await service.GenerateAsync(2024, 10, "tester", "u1");
        var run2Items = ctx.PayrollItems.Where(i => i.PayrollRunId == r2.PayrollRunId).ToList();
        Assert.Single(run2Items);
        Assert.Equal(500m, run2Items[0].LoansTotal);
        Assert.Equal(100m, run2Items[0].DeductionsTotal);

        // اعتماد الكشف يمنع إعادة الاحتساب (وبالتالي يمنع الخصم المتكرر)
        await service.ApproveAsync(r2.PayrollRunId, "tester", "u1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(2024, 10, "tester", "u1"));

        // بعد الاعتماد، لا يمكن خصم السلفة في كشف آخر لنفس الشهر
        Assert.Equal(1, ctx.PayrollRuns.Count(r => r.Status == PayrollStatus.Approved));
    }

    [Fact]
    public async Task Overtime_Requires_Approval_Before_Being_Paid()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(salary: 2400m)); // سعر الساعة = 10
        await ctx.SaveChangesAsync();

        var service = new PayrollService(ctx, new SettingsService(ctx), new AuditService(ctx), NullLogger<PayrollService>.Instance);

        // بدون اعتماد إضافي: لا تُضاف قيمة إضافي
        var r1 = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item1 = ctx.PayrollItems.Single(i => i.PayrollRunId == r1.PayrollRunId);
        Assert.Equal(0m, item1.OvertimeValue);

        // بعد إلغاء الاعتماد وإضافة اعتماد إضافي
        await service.CancelAsync(r1.PayrollRunId, "tester", "u1");

        ctx.OvertimeApprovals.Add(new OvertimeApproval
        {
            EmployeeId = 1,
            PeriodStart = new DateTime(2024, 10, 1),
            PeriodEnd = new DateTime(2024, 10, 31),
            CalculatedHours = 10m,
            ApprovedHours = 8m,
            OvertimeMultiplier = 1.5m,
            Status = ApprovalStatus.Approved
        });
        await ctx.SaveChangesAsync();

        var r2 = await service.GenerateAsync(2024, 10, "tester", "u1");
        var item2 = ctx.PayrollItems.Single(i => i.PayrollRunId == r2.PayrollRunId);
        Assert.Equal(8m, item2.ApprovedOvertimeHours);
        Assert.Equal(120m, item2.OvertimeValue); // 8 * 10 * 1.5
    }
}
