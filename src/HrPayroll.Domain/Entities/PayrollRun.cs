using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>كشف رواتب شهري (تشغيل واحد).</summary>
public class PayrollRun : BaseEntity
{
    public int Year { get; set; }
    public int Month { get; set; }

    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;

    public DateTime? ComputedAt { get; set; }
    public string? ComputedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    public decimal TotalNet { get; set; }
    public decimal TotalOvertime { get; set; }
    public decimal TotalDeductions { get; set; }

    public string? Notes { get; set; }

    /// <summary>رقم الإصدار (لحفظ تاريخ كل إصدار).</summary>
    public int Version { get; set; } = 1;

    public ICollection<PayrollItem> Items { get; set; } = new List<PayrollItem>();
}
