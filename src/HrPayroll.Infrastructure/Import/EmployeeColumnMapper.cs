namespace HrPayroll.Infrastructure.Import;

/// <summary>يكتشف أعمدة ملف الموظفين الإلكتروني ويربطها بحقول النظام.</summary>
public static class EmployeeColumnMapper
{
    private static readonly (EmployeeField Field, string[] Aliases)[] Map =
    {
        (EmployeeField.DeviceEmployeeCode, new[] { "devicecode", "biometric", "biometriccode", "machinecode", "deviceid", "كودالجهاز", "كودالبصمة", "رقمالجهاز", "الرقمالبصمة" }),
        (EmployeeField.EmployeeCode, new[] { "employeecode", "empcode", "code", "id", "no", "number", "كود", "كودالموظف", "رقمالوظيفي", "الرقمالوظيفي", "رقمالموظف", "م", "الرقم" }),
        (EmployeeField.NationalId, new[] { "nationalid", "ssn", "idnumber", "nationalnumber", "الرقمالقومي", "الرقمالوطني", "الرقمالمدني" }),
        (EmployeeField.FullName, new[] { "employeename", "fullname", "name", "الاسم", "اسمالموظف", "الاسمكامل", "الاسمبالكامل" }),
        (EmployeeField.Department, new[] { "department", "dept", "section", "division", "القسم", "الادارة", "الادارةالقسم" }),
        (EmployeeField.JobTitle, new[] { "jobtitle", "position", "title", "designation", "المسمى", "المسمىالوظيفي", "الوظيفة", "المهنة", "الدرجةالوظيفية" }),
        (EmployeeField.DailyRate, new[] { "dailyrate", "dayrate", "rateperday", "الاجراليومي", "سعراليوم", "اليومية", "الاجراليومى" }),
        (EmployeeField.BasicSalary, new[] { "basicsalary", "monthlysalary", "salary", "basicmonthlysalary", "wage", "الراتب", "الراتبالاساسي", "الراتبالشهري", "الاجر", "الاجرالاساسي", "المرتب" }),
        (EmployeeField.HireDate, new[] { "hiredate", "joindate", "startdate", "dateofjoin", "hiring date", "تاريخالتعيين", "تاريخالالتحاق", "تاريخالبداية", "تاريخالتعاقد" }),
        (EmployeeField.Phone, new[] { "phone", "mobile", "telephone", "tel", "contact", "الهاتف", "الجوال", "الموبايل", "رقمالهاتف", "التليفون" }),
        (EmployeeField.Email, new[] { "email", "mail", "emailaddress", "البريد", "البريدالالكتروني", "الايميل" }),
        (EmployeeField.StandardWorkHours, new[] { "standardworkhours", "workhours", "dailyhours", "hours", "ساعاتالعمل", "ساعاتالعملاليومية" }),
        (EmployeeField.AnnualLeaveEntitlement, new[] { "annualleaveentitlement", "annualleave", "leavebalance", "leaveentitlement", "الاجازةالسنوية", "رصيدالاجازة", "رصيدالاجازات" }),
        (EmployeeField.Notes, new[] { "notes", "note", "remark", "remarks", "comment", "ملاحظات", "ملاحظة", "بياناتاضافية" })
    };

    public static string Normalize(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;
        var sb = new System.Text.StringBuilder(header.Length);
        foreach (var ch in header.Trim().ToLowerInvariant())
        {
            if (ch == ' ' || ch == '_' || ch == '-' || ch == '.' || ch == '(' || ch == ')') continue;
            if (char.IsLetterOrDigit(ch) || ch >= '\u0600') sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>يبني خريطة الأعمدة من صف العناوين.</summary>
    public static EmployeeColumnMapping? Detect(IReadOnlyList<string[]> rows, int maxScanRows = 15)
    {
        var limit = Math.Min(rows.Count, maxScanRows);
        EmployeeColumnMapping? best = null;
        var bestScore = -1;

        for (var r = 0; r < limit; r++)
        {
            var map = new EmployeeColumnMapping { HeaderRowIndex = r };
            var used = new HashSet<int>();
            var score = 0;

            // المرحلة 1: تطابق تام
            foreach (var (field, aliases) in Map)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (used.Contains(c)) continue;
                    var h = Normalize(rows[r][c]);
                    if (h.Length == 0) continue;
                    if (aliases.Any(a => h == a))
                    {
                        map.Columns[field] = c;
                        used.Add(c);
                        score += 3;
                        break;
                    }
                }
            }

            // المرحلة 2: احتواء جزئي
            foreach (var (field, aliases) in Map)
            {
                if (map.Columns.ContainsKey(field)) continue;
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (used.Contains(c)) continue;
                    var h = Normalize(rows[r][c]);
                    if (h.Length == 0) continue;
                    if (aliases.Any(a => h.Contains(a) || a.Contains(h)))
                    {
                        map.Columns[field] = c;
                        used.Add(c);
                        score += 1;
                        break;
                    }
                }
            }

            if (map.IsUsable && score > bestScore)
            {
                bestScore = score;
                best = map;
            }
        }

        return best;
    }
}

/// <summary>تحويل الأرقام والتواريخ في ملفات الموظفين.</summary>
public static class CellValueParser
{
    private static readonly char[] Ignored = { ',', ' ', '\u00A0', '\u200f', '\u200e', '\t', '"' };

    /// <summary>يحوّل الأرقام العربية-الهندية إلى أرقام لاتينية.</summary>
    public static string NormalizeDigits(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));       // ٠-٩
            else if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));   // ۰-۹
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>يحاول قراءة مبلغ مالي من خلية نصية (يدعم الفواصل والرموز).</summary>
    public static decimal? TryParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = NormalizeDigits(raw.Trim().Trim('"'));
        foreach (var c in new[] { "EGP", "SAR", "USD", "ج.م", "جنيه", "ريال", "$", "£", "€" })
            text = text.Replace(c, string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Trim();
        foreach (var ch in Ignored) text = text.Replace(ch.ToString(), string.Empty);
        if (text.Length == 0) return null;
        return decimal.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
