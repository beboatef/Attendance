using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Services;

namespace HrPayroll.Web.ViewModels;

/// <summary>نموذج شاشة "كشف المرتبات الشهري" — الجدول الرئيسي القابل للتحرير.</summary>
public class PayrollIndexViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<PayrollRun> Runs { get; set; } = new();
    public PayrollRun? CurrentRun { get; set; }
    public PayrollReadiness? Readiness { get; set; }
    public List<Domain.Entities.Employee> Employees { get; set; } = new();
    public List<PayrollReportRow> Rows { get; set; } = new();
    public string? Department { get; set; }
    public int? EmployeeId { get; set; }
    public string? Search { get; set; }
    public PayrollStatus? Status { get; set; }
    public List<string> Departments { get; set; } = new();

    /// <summary>هل يمكن تعديل خلايا الكشف في حالته الحالية؟</summary>
    public bool CanEdit => CurrentRun is not null && PayrollSheetService.IsEditable(CurrentRun.Status);

    public string PeriodText { get; set; } = string.Empty;

    public decimal TotalBasic => Rows.Sum(r => r.BasicSalary);
    public decimal TotalOvertime => Rows.Sum(r => r.OvertimeAmount);
    public decimal TotalBonuses => Rows.Sum(r => r.Bonuses + r.Allowances + r.AdditionalDaysAmount);
    public decimal TotalDeductions => Rows.Sum(r => r.TotalDeductions);
    public decimal TotalGross => Rows.Sum(r => r.GrossEarnings);
    public decimal TotalNet => Rows.Sum(r => r.NetSalary);
    public int LateMinutesTotal => Rows.Sum(r => r.LateMinutes);
    public int ReviewCount => Rows.Count(r => r.NeedsReview);
    public decimal AnnualLeaveDaysTotal => Rows.Sum(r => r.AnnualLeaveDays);
}

public class PayrollDetailsViewModel
{
    public PayrollRun Run { get; set; } = new();
    public List<PayrollReportRow> Rows { get; set; } = new();
    public string? Department { get; set; }
    public List<string> Departments { get; set; } = new();

    public decimal TotalBasic => Rows.Sum(r => r.BasicSalary);
    public decimal TotalOvertime => Rows.Sum(r => r.OvertimeAmount);
    public decimal TotalBonuses => Rows.Sum(r => r.Bonuses + r.Allowances + r.AdditionalDaysAmount);
    public decimal TotalDeductions => Rows.Sum(r => r.TotalDeductions);
    public decimal TotalNet => Rows.Sum(r => r.NetSalary);
    public int TotalEmployees => Rows.Count;
}

/// <summary>طلب حفظ تعديلات كشف المرتبات (JSON).</summary>
public class PayrollSheetSaveRequest
{
    public int RunId { get; set; }

    /// <summary>تأكيد صريح لتجاوز رصيد الإجازة السنوية عند عدم كفايته.</summary>
    public bool AllowOverdraft { get; set; }

    public List<PayrollRowEditInput> Rows { get; set; } = new();
}

/// <summary>تعديل سطر واحد قادم من الجدول.</summary>
public class PayrollRowEditInput
{
    public int PayrollItemId { get; set; }
    public decimal? ApprovedAbsenceDays { get; set; }
    public decimal? AnnualLeaveDays { get; set; }
    public decimal? OtherUnpaidLeaveDays { get; set; }
    public decimal? OvertimeHours { get; set; }
    public decimal? Bonuses { get; set; }
    public decimal? Allowances { get; set; }
    public decimal? Advances { get; set; }
    public decimal? OtherDeductions { get; set; }
    public decimal? ExtraDays { get; set; }
    public decimal? ExtraDaysRate { get; set; }

    public PayrollRowEdit ToServiceModel() => new()
    {
        PayrollItemId = PayrollItemId,
        ApprovedAbsenceDays = ApprovedAbsenceDays,
        AnnualLeaveDays = AnnualLeaveDays,
        OtherUnpaidLeaveDays = OtherUnpaidLeaveDays,
        OvertimeHours = OvertimeHours,
        Bonuses = Bonuses,
        Allowances = Allowances,
        Advances = Advances,
        OtherDeductions = OtherDeductions,
        ExtraDays = ExtraDays,
        ExtraDaysRate = ExtraDaysRate
    };
}
