using HrPayroll.Domain.Models;

namespace HrPayroll.Domain.Services;

/// <summary>
/// حساب قيم الرواتب. منطق نقي قابل للاختبار بالكامل.
///
/// سياسة الشركة الافتراضية:
///   الأجر اليومي = الراتب الأساسي / 30
///   الأجر بالساعة = الأجر اليومي / 8
///   الإضافي = ساعات الإضافي المعتمدة × الأجر بالساعة (بدون أي مضاعف)
///   الخصم اليومي للغياب/الإجازة بدون أجر = الأجر اليومي
/// </summary>
public static class PayrollCalculator
{
    /// <summary>تقريب القيم المالية إلى منزلتين عشريتين.</summary>
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>يحسب كشف راتب موظف واحد بكل مكوّناته مفصولة.</summary>
    public static PayrollComputation Compute(PayrollInputs inputs, AttendancePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(policy);
        if (inputs.BasicSalary < 0) throw new ArgumentOutOfRangeException(nameof(inputs));

        var dailyRate = policy.DailyRateFor(inputs.BasicSalary, inputs.DailyRateOverride);
        var hourlyRate = policy.HourlyRateFor(dailyRate);
        var minuteRate = policy.MinuteRateFor(dailyRate);

        // ---------- التأخير ----------
        var lateMinutes = Math.Max(0, inputs.LateMinutes);
        var lateDeduction = policy.LateDeductionEnabled ? Round2(lateMinutes * minuteRate) : 0m;

        // ---------- الإضافي ----------
        var multiplier = inputs.OvertimeMultiplier <= 0m ? 1m : inputs.OvertimeMultiplier;

        var rawMinutes = Math.Max(0, inputs.RawOvertimeMinutes);
        var approvedMinutes = Math.Max(0, inputs.ApprovedOvertimeMinutes);
        var paidMinutes = inputs.PaidOvertimeMinutes > 0
            ? Math.Max(0, inputs.PaidOvertimeMinutes)
            : approvedMinutes;

        // الإضافي المدفوع لا يتجاوز المعتمد، والمعتمد لا يتجاوز الخام
        if (approvedMinutes > rawMinutes && rawMinutes > 0) approvedMinutes = rawMinutes;

        var rawHours = Round2(rawMinutes / 60m);
        var approvedHours = Round2(approvedMinutes / 60m);
        var paidHours = Round2(paidMinutes / 60m);

        var overtimeAmount = OvertimeValueForMinutes(paidMinutes, hourlyRate, multiplier);

        // ---------- الغياب والإجازات ----------
        var absenceDays = Math.Max(0m, inputs.AbsenceDays);
        var unpaidLeaveDays = Math.Max(0m, inputs.UnpaidLeaveDays);

        var absenceDeduction = policy.AbsenceDeductionEnabled ? Round2(absenceDays * dailyRate) : 0m;
        var unpaidLeaveDeduction = policy.UnpaidLeaveDeductionEnabled
            ? Round2(unpaidLeaveDays * dailyRate)
            : 0m;

        // ---------- الإضافات ----------
        var additionalDaysRate = inputs.AdditionalDaysRate is > 0m
            ? inputs.AdditionalDaysRate.Value
            : dailyRate;
        var additionalDaysAmount = Round2(Math.Max(0m, inputs.AdditionalDays) * additionalDaysRate);

        var bonuses = Round2(Math.Max(0m, inputs.Bonuses));
        var allowances = Round2(Math.Max(0m, inputs.Allowances));

        // الراتب الشهري يشمل بالفعل أيام الراحة المدفوعة؛ لذلك لا تُضاف قيمتها مرة أخرى.
        var grossEarnings = Round2(inputs.BasicSalary + overtimeAmount + bonuses + allowances + additionalDaysAmount);

        var advances = Round2(Math.Max(0m, inputs.Advances));
        var deductionsTotal = Round2(Math.Max(0m, inputs.DeductionsTotal));
        var otherDeductions = Round2(Math.Max(0m, inputs.OtherDeductions));

        var totalDeductions = Round2(lateDeduction + absenceDeduction + unpaidLeaveDeduction
                                     + advances + deductionsTotal + otherDeductions);

        var netSalary = Round2(grossEarnings - totalDeductions);
        if (netSalary < 0m) netSalary = 0m;

        var details = PayrollComputation.SerializeDetails(new
        {
            policy = new
            {
                dailyRateBasisDays = policy.WorkDaysPerMonth,
                workHoursPerDay = policy.WorkHoursPerDay,
                overtimeMultiplier = multiplier,
                lateDeductionEnabled = policy.LateDeductionEnabled,
                absenceDeductionEnabled = policy.AbsenceDeductionEnabled,
                unpaidLeaveDeductionEnabled = policy.UnpaidLeaveDeductionEnabled
            },
            rates = new { basicSalary = inputs.BasicSalary, dailyRate, hourlyRate, minuteRate },
            attendance = new
            {
                attendanceDays = inputs.AttendanceDays,
                paidRestDays = inputs.PaidRestDays,
                paidLeaveDays = inputs.PaidLeaveDays,
                unpaidLeaveDays,
                absenceDays
            },
            overtime = new
            {
                rawMinutes, rawHours,
                approvedMinutes, approvedHours,
                paidMinutes, paidHours,
                rateUsed = hourlyRate, multiplier,
                amount = overtimeAmount
            },
            deductions = new
            {
                late = new { minutes = lateMinutes, rate = minuteRate, amount = lateDeduction },
                absence = new { days = absenceDays, rate = dailyRate, amount = absenceDeduction },
                unpaidLeave = new { days = unpaidLeaveDays, rate = dailyRate, amount = unpaidLeaveDeduction },
                advances, otherDeductions, deductionsTotal
            },
            earnings = new
            {
                basic = inputs.BasicSalary, overtime = overtimeAmount,
                bonuses, allowances,
                additionalDays = inputs.AdditionalDays,
                additionalDaysRate, additionalDaysAmount
            },
            totals = new { grossEarnings, totalDeductions, netSalary }
        });

        return new PayrollComputation(
            BasicSalary: inputs.BasicSalary,
            DailyRate: dailyRate,
            HourlyRate: hourlyRate,
            MinuteRate: minuteRate,
            DailyRateBasisDays: policy.WorkDaysPerMonth,
            WorkHoursPerDay: policy.WorkHoursPerDay,
            AttendanceDays: Math.Max(0, inputs.AttendanceDays),
            PaidRestDays: Math.Max(0, inputs.PaidRestDays),
            PaidLeaveDays: Math.Max(0m, inputs.PaidLeaveDays),
            UnpaidLeaveDays: unpaidLeaveDays,
            AbsenceDays: absenceDays,
            LateMinutes: lateMinutes,
            LateDeduction: lateDeduction,
            RawOvertimeMinutes: rawMinutes,
            RawOvertimeHours: rawHours,
            ApprovedOvertimeHours: approvedHours,
            PaidOvertimeHours: paidHours,
            OvertimeMultiplier: multiplier,
            OvertimeAmount: overtimeAmount,
            AbsenceDeduction: absenceDeduction,
            UnpaidLeaveDeduction: unpaidLeaveDeduction,
            Advances: advances,
            DeductionsTotal: deductionsTotal,
            OtherDeductions: otherDeductions,
            Bonuses: bonuses,
            Allowances: allowances,
            AdditionalDaysCount: Math.Max(0m, inputs.AdditionalDays),
            AdditionalDaysAmount: additionalDaysAmount,
            GrossEarnings: grossEarnings,
            TotalDeductions: totalDeductions,
            NetSalary: netSalary,
            CalculationDetails: details);
    }

    /// <summary>
    /// قيمة الإضافي بالدقائق المدفوعة: تُحوَّل الدقائق إلى ساعات (بمنزلتين عشريتين)
    /// ثم تُضرب في الأجر بالساعة. هذه هي المعادلة الوحيدة المستخدمة في التطبيق
    /// (كشف الراتب + شاشة اعتماد الإضافي) حتى تتطابق الأرقام المعروضة مع المحسوبة.
    /// </summary>
    public static decimal OvertimeValueForMinutes(int payableMinutes, decimal hourlyRate, decimal multiplier = 1m)
    {
        var m = multiplier <= 0m ? 1m : multiplier;
        if (payableMinutes <= 0 || hourlyRate <= 0m) return 0m;
        return Round2(Round2(payableMinutes / 60m) * hourlyRate * m);
    }

    /// <summary>قيمة الإضافي وحدها (بدون أي مضاعف افتراضيًا).</summary>
    public static decimal CalculateOvertimeValue(
        decimal basicSalary, decimal approvedHours, decimal multiplier, AttendancePolicy policy,
        decimal? dailyRateOverride = null)
    {
        var hourly = policy.HourlyRateOfSalary(basicSalary, dailyRateOverride);
        var m = multiplier <= 0m ? 1m : multiplier;
        return Round2(Math.Max(0m, approvedHours) * hourly * m);
    }

    /// <summary>قيمة الإضافي وحدها من دقائق معتمدة (يستخدمها كشف الراتب وشاشة الاعتماد).</summary>
    public static decimal CalculateOvertimeValueFromMinutes(
        decimal basicSalary, int approvedMinutes, decimal multiplier, AttendancePolicy policy,
        decimal? dailyRateOverride = null)
        => OvertimeValueForMinutes(approvedMinutes, policy.HourlyRateOfSalary(basicSalary, dailyRateOverride), multiplier);
}
