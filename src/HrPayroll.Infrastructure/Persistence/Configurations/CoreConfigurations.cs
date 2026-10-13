using HrPayroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPayroll.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("Departments");
        b.Property(x => x.Name).IsRequired().HasMaxLength(150);
        b.Property(x => x.Code).HasMaxLength(50);
        b.Property(x => x.ManagerName).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class JobTitleConfiguration : IEntityTypeConfiguration<JobTitle>
{
    public void Configure(EntityTypeBuilder<JobTitle> b)
    {
        b.ToTable("JobTitles");
        b.Property(x => x.Name).IsRequired().HasMaxLength(150);
        b.Property(x => x.Code).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.Name).IsUnique();

        b.HasOne(x => x.Department).WithMany(d => d.JobTitles)
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PublicHolidayConfiguration : IEntityTypeConfiguration<PublicHoliday>
{
    public void Configure(EntityTypeBuilder<PublicHoliday> b)
    {
        b.ToTable("PublicHolidays");
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.HolidayDate).HasColumnType("datetime2");
        b.HasIndex(x => x.HolidayDate);
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("Employees");
        b.Property(x => x.EmployeeCode).IsRequired().HasMaxLength(50);
        b.Property(x => x.DeviceEmployeeCode).HasMaxLength(50);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        b.Property(x => x.JobTitle).HasMaxLength(150);
        b.Property(x => x.Department).HasMaxLength(150);
        b.Property(x => x.BasicSalary).HasColumnType("decimal(18,2)");
        b.Property(x => x.DailyRate).HasColumnType("decimal(18,4)");
        b.Property(x => x.StandardWorkHours).HasColumnType("decimal(5,2)");
        b.Property(x => x.AnnualLeaveEntitlement).HasColumnType("decimal(6,2)");
        b.Property(x => x.AnnualLeaveCarriedOver).HasColumnType("decimal(6,2)");
        b.Property(x => x.AnnualLeaveBalance).HasColumnType("decimal(6,2)");
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.ContactInfo).HasMaxLength(500);
        b.Property(x => x.NationalId).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(1000);

        // كود الموظف فريد. فهرس كود الجهاز الفريد يُضاف في AppDbContext
        // لأن صيغة الفلتر تختلف بين مزوّدي قواعد البيانات.
        b.HasIndex(x => x.EmployeeCode).IsUnique();
        b.HasIndex(x => x.IsActive);
        b.HasIndex(x => x.DepartmentId);

        b.HasOne(x => x.Shift).WithMany(s => s.Employees)
            .HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.DepartmentRef).WithMany(d => d.Employees)
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.JobTitleRef).WithMany(j => j.Employees)
            .HasForeignKey(x => x.JobTitleId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.ToTable("Shifts");
        b.Property(x => x.ShiftName).IsRequired().HasMaxLength(150);
        b.Property(x => x.OvertimeMultiplier).HasColumnType("decimal(18,4)");
        b.Property(x => x.StandardHours).HasColumnType("decimal(5,2)");
        b.Property(x => x.WorkingDays).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

public class EmployeeShiftAssignmentConfiguration : IEntityTypeConfiguration<EmployeeShiftAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeShiftAssignment> b)
    {
        b.ToTable("EmployeeShiftAssignments");
        b.Property(x => x.Notes).HasMaxLength(500);

        b.HasOne(x => x.Employee).WithMany(e => e.ShiftAssignments)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Shift).WithMany(s => s.Assignments)
            .HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
    }
}

public class RawPunchConfiguration : IEntityTypeConfiguration<RawPunch>
{
    public void Configure(EntityTypeBuilder<RawPunch> b)
    {
        b.ToTable("RawPunches");
        b.Property(x => x.DeviceEmployeeCode).IsRequired().HasMaxLength(50);
        b.Property(x => x.DeviceId).HasMaxLength(50);
        b.Property(x => x.RawEmployeeName).HasMaxLength(200);
        b.Property(x => x.AttType).HasMaxLength(50);
        b.Property(x => x.RawPunchTimeText).HasMaxLength(100);
        b.Property(x => x.DedupHash).IsRequired().HasMaxLength(200);
        b.Property(x => x.ValidationMessage).HasMaxLength(500);
        b.Property(x => x.PunchTime).HasColumnType("datetime2");

        // منع استيراد نفس البصمة مرتين (حتى بين ملفات مختلفة)
        b.HasIndex(x => x.DedupHash).IsUnique();
        b.HasIndex(x => new { x.EmployeeId, x.PunchTime });
        b.HasIndex(x => x.DeviceEmployeeCode);

        b.HasOne(x => x.ImportBatch).WithMany(i => i.RawPunches)
            .HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Employee).WithMany(e => e.RawPunches)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class AttendanceEventConfiguration : IEntityTypeConfiguration<AttendanceEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceEvent> b)
    {
        b.ToTable("AttendanceEvents");
        b.Property(x => x.Confidence).HasColumnType("decimal(5,4)");
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.EventTime).HasColumnType("datetime2");
        b.Property(x => x.WorkDate).HasColumnType("datetime2");

        // كل بصمة خام لها حدث واحد على الأكثر
        b.HasIndex(x => x.RawPunchId).IsUnique();
        b.HasIndex(x => new { x.EmployeeId, x.WorkDate });

        b.HasOne(x => x.Employee).WithMany(e => e.AttendanceEvents)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.RawPunch).WithOne(p => p.AttendanceEvent)
            .HasForeignKey<AttendanceEvent>(x => x.RawPunchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class DailyAttendanceConfiguration : IEntityTypeConfiguration<DailyAttendance>
{
    public void Configure(EntityTypeBuilder<DailyAttendance> b)
    {
        b.ToTable("DailyAttendances");
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.AdminNotes).HasMaxLength(1000);
        b.Property(x => x.NeedsReviewReason).HasMaxLength(500);
        b.Property(x => x.ShiftStart).HasColumnType("datetime2");
        b.Property(x => x.ShiftEnd).HasColumnType("datetime2");
        b.Property(x => x.CheckInTime).HasColumnType("datetime2");
        b.Property(x => x.CheckOutTime).HasColumnType("datetime2");
        b.Property(x => x.WorkDate).HasColumnType("datetime2");

        b.HasIndex(x => new { x.EmployeeId, x.WorkDate }).IsUnique();
        b.HasIndex(x => x.WorkDate);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.HolidayId);

        b.HasOne(x => x.Employee).WithMany(e => e.DailyAttendances)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Holiday).WithMany().HasForeignKey(x => x.HolidayId).OnDelete(DeleteBehavior.SetNull);
        // NoAction وليس SetNull: مسار الحذف المتسلسل (Employees → LeaveRequests → DailyAttendances)
        // يتقاطع مع (Employees → DailyAttendances) فيرفضه SQL Server كمسارات حذف متعددة.
        b.HasOne(x => x.LeaveRequest).WithMany().HasForeignKey(x => x.LeaveRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.ToTable("LeaveRequests");
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.ApprovedBy).HasMaxLength(200);
        b.Property(x => x.WorkingDays).HasColumnType("decimal(6,2)");

        b.HasOne(x => x.Employee).WithMany(e => e.LeaveRequests)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PayrollItem).WithMany(p => p.LeaveRequests)
            .HasForeignKey(x => x.PayrollItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.StartDate, x.EndDate });
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.PayrollItemId);
    }
}

public class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> b)
    {
        b.ToTable("AttendanceCorrections");
        b.Property(x => x.FieldName).IsRequired().HasMaxLength(100);
        b.Property(x => x.OldValue).HasMaxLength(200);
        b.Property(x => x.NewValue).HasMaxLength(200);
        b.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        b.Property(x => x.ApprovedBy).HasMaxLength(200);
        b.Property(x => x.WorkDate).HasColumnType("datetime2");

        b.HasOne(x => x.DailyAttendance).WithMany(d => d.Corrections)
            .HasForeignKey(x => x.DailyAttendanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Employee).WithMany(e => e.Corrections)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmployeeId, x.WorkDate });
    }
}
