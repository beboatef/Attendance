using HrPayroll.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Persistence;

/// <summary>سياق قاعدة البيانات الرئيسي (SQL Server).</summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<EmployeeShiftAssignment> EmployeeShiftAssignments => Set<EmployeeShiftAssignment>();
    public DbSet<RawPunch> RawPunches => Set<RawPunch>();
    public DbSet<AttendanceEvent> AttendanceEvents => Set<AttendanceEvent>();
    public DbSet<DailyAttendance> DailyAttendances => Set<DailyAttendance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Deduction> Deductions => Set<Deduction>();
    public DbSet<OvertimeApproval> OvertimeApprovals => Set<OvertimeApproval>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollItem> PayrollItems => Set<PayrollItem>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // كود الجهاز/البصمة فريد لمنع ربط بصمة واحدة بموظفين مختلفين.
        // على SQL Server نستخدم فهرسًا مُفلترًا لأن قيم NULL المتعددة مسموحة عند الفلترة،
        // بينما SQLite يعتبر NULL مميزًا أصلًا في الفهرس الفريد.
        var employee = builder.Entity<Employee>();
        if (Database.IsSqlServer())
            employee.HasIndex(e => e.DeviceEmployeeCode).IsUnique().HasFilter("[DeviceEmployeeCode] IS NOT NULL");
        else
            employee.HasIndex(e => e.DeviceEmployeeCode).IsUnique();
    }
}
