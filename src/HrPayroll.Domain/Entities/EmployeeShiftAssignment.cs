namespace HrPayroll.Domain.Entities;

/// <summary>إسناد وردية لموظف خلال فترة سريان (يدعم الورديات المتغيرة).</summary>
public class EmployeeShiftAssignment : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsPrimary { get; set; } = true;
    public string? Notes { get; set; }
}
