namespace HrPayroll.Domain.Entities;

/// <summary>الكيان الأساسي الذي يحتوي على بيانات التتبع للجميع.</summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
