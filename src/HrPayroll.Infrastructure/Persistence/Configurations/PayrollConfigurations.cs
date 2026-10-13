using HrPayroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPayroll.Infrastructure.Persistence.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> b)
    {
        b.ToTable("Loans");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Employee).WithMany(e => e.Loans)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PayrollItem).WithMany(p => p.Loans)
            .HasForeignKey(x => x.PayrollItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.DeductionYear, x.DeductionMonth });
        b.HasIndex(x => x.PayrollItemId);
    }
}

public class DeductionConfiguration : IEntityTypeConfiguration<Deduction>
{
    public void Configure(EntityTypeBuilder<Deduction> b)
    {
        b.ToTable("Deductions");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        b.Property(x => x.ApprovedBy).HasMaxLength(200);

        b.HasOne(x => x.Employee).WithMany(e => e.Deductions)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PayrollItem).WithMany(p => p.Deductions)
            .HasForeignKey(x => x.PayrollItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.Year, x.Month });
        b.HasIndex(x => x.PayrollItemId);
    }
}

public class OvertimeApprovalConfiguration : IEntityTypeConfiguration<OvertimeApproval>
{
    public void Configure(EntityTypeBuilder<OvertimeApproval> b)
    {
        b.ToTable("OvertimeApprovals");
        b.Property(x => x.CalculatedHours).HasColumnType("decimal(18,2)");
        b.Property(x => x.ApprovedHours).HasColumnType("decimal(18,2)");
        b.Property(x => x.OvertimeMultiplier).HasColumnType("decimal(18,4)");
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ApprovedBy).HasMaxLength(200);

        b.HasOne(x => x.Employee).WithMany(e => e.OvertimeApprovals)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PayrollItem).WithMany(p => p.OvertimeApprovals)
            .HasForeignKey(x => x.PayrollItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.PeriodStart, x.PeriodEnd });
        b.HasIndex(x => x.PayrollItemId);
    }
}

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("PayrollRuns");
        b.Property(x => x.TotalNet).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalOvertime).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalDeductions).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalGross).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalBonuses).HasColumnType("decimal(18,2)");
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ComputedBy).HasMaxLength(200);
        b.Property(x => x.ApprovedBy).HasMaxLength(200);
        b.Property(x => x.LockedBy).HasMaxLength(200);
        b.Property(x => x.ReopenedBy).HasMaxLength(200);
        b.Property(x => x.ReopenReason).HasMaxLength(1000);

        b.HasIndex(x => new { x.Year, x.Month, x.Version }).IsUnique();
    }
}

public class PayrollItemConfiguration : IEntityTypeConfiguration<PayrollItem>
{
    public void Configure(EntityTypeBuilder<PayrollItem> b)
    {
        b.ToTable("PayrollItems");
        b.Property(x => x.EmployeeCode).IsRequired().HasMaxLength(50);
        b.Property(x => x.EmployeeName).IsRequired().HasMaxLength(200);
        b.Property(x => x.DepartmentName).HasMaxLength(150);
        b.Property(x => x.JobTitleName).HasMaxLength(150);
        b.Property(x => x.BasicSalary).HasColumnType("decimal(18,2)");
        b.Property(x => x.DailyRate).HasColumnType("decimal(18,4)");
        b.Property(x => x.HourlyRate).HasColumnType("decimal(18,4)");
        b.Property(x => x.MinuteRate).HasColumnType("decimal(18,8)");
        b.Property(x => x.PaidLeaveDays).HasColumnType("decimal(6,2)");
        b.Property(x => x.UnpaidLeaveDays).HasColumnType("decimal(6,2)");
        b.Property(x => x.AbsenceDays).HasColumnType("decimal(6,2)");
        b.Property(x => x.OriginalAbsenceDays).HasColumnType("decimal(6,2)");
        b.Property(x => x.AnnualLeaveDays).HasColumnType("decimal(6,2)");
        b.Property(x => x.AnnualLeaveBalanceApplied).HasColumnType("decimal(6,2)");
        b.Property(x => x.ManualOvertimeHours).HasColumnType("decimal(18,2)");
        b.Property(x => x.LastEditedBy).HasMaxLength(200);
        b.Property(x => x.LateDeduction).HasColumnType("decimal(18,2)");
        b.Property(x => x.AbsenceDeduction).HasColumnType("decimal(18,2)");
        b.Property(x => x.UnpaidLeaveDeduction).HasColumnType("decimal(18,2)");
        b.Property(x => x.RawOvertimeHours).HasColumnType("decimal(18,2)");
        b.Property(x => x.ApprovedOvertimeHours).HasColumnType("decimal(18,2)");
        b.Property(x => x.PaidOvertimeHours).HasColumnType("decimal(18,2)");
        b.Property(x => x.OvertimeMultiplier).HasColumnType("decimal(18,4)");
        b.Property(x => x.OvertimeAmount).HasColumnType("decimal(18,2)").HasColumnName("OvertimeValue");
        b.Property(x => x.LoansTotal).HasColumnType("decimal(18,2)");
        b.Property(x => x.DeductionsTotal).HasColumnType("decimal(18,2)");
        b.Property(x => x.OtherDeductions).HasColumnType("decimal(18,2)");
        b.Property(x => x.Bonuses).HasColumnType("decimal(18,2)");
        b.Property(x => x.Allowances).HasColumnType("decimal(18,2)");
        b.Property(x => x.AdditionalDaysCount).HasColumnType("decimal(6,2)");
        b.Property(x => x.AdditionalDaysRate).HasColumnType("decimal(18,4)");
        b.Property(x => x.AdditionalDaysAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.GrossEarnings).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalDeductions).HasColumnType("decimal(18,2)");
        b.Property(x => x.GrossSalary).HasColumnType("decimal(18,2)");
        b.Property(x => x.NetSalary).HasColumnType("decimal(18,2)");
        // يبقى بلا نوع أعمدة صريح: SQL Server يحوّله إلى nvarchar(max) تلقائيًا،
        // بينما SQLite (المستخدم في الاختبارات) لا يقبل الصيغة nvarchar(max).
        b.Property(x => x.CalculationDetails);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.PayrollRun).WithMany(r => r.Items)
            .HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Employee).WithMany(e => e.PayrollItems)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.EmployeeId, x.EmployeeCode });
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("ImportBatches");
        b.Property(x => x.FileName).IsRequired().HasMaxLength(300);
        b.Property(x => x.FileHash).HasMaxLength(128);
        b.Property(x => x.ImportedByUserId).HasMaxLength(450);
        b.Property(x => x.ImportedByName).HasMaxLength(200);
        b.Property(x => x.DetectedFormat).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.FileHash);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.Action).IsRequired().HasMaxLength(100);
        b.Property(x => x.EntityName).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.UserId).HasMaxLength(450);
        b.Property(x => x.UserName).HasMaxLength(200);
        b.Property(x => x.OldValue).HasMaxLength(4000);
        b.Property(x => x.NewValue).HasMaxLength(4000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => x.Timestamp);
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("AppSettings");
        b.Property(x => x.Key).IsRequired().HasMaxLength(200);
        b.Property(x => x.Value).IsRequired().HasMaxLength(2000);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Group).HasMaxLength(100);
        b.Property(x => x.DataType).HasMaxLength(20);
        b.HasIndex(x => x.Key).IsUnique();
    }
}

public class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> b)
    {
        b.ToTable("PayrollAdjustments");
        b.Property(x => x.Description).IsRequired().HasMaxLength(500);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Days).HasColumnType("decimal(6,2)");
        b.Property(x => x.DailyRateOverride).HasColumnType("decimal(18,4)");
        b.Property(x => x.ApprovedBy).HasMaxLength(200);
        b.Property(x => x.ReferenceDate).HasColumnType("datetime2");

        b.HasOne(x => x.Employee).WithMany(e => e.Adjustments)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PayrollItem).WithMany(p => p.Adjustments)
            .HasForeignKey(x => x.PayrollItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.Year, x.Month });
        b.HasIndex(x => x.PayrollItemId);
    }
}
