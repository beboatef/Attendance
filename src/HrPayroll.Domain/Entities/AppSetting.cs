namespace HrPayroll.Domain.Entities;

/// <summary>إعدادات النظام القابلة للتعديل.</summary>
public class AppSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public string? DataType { get; set; } // string/int/decimal/bool
}
