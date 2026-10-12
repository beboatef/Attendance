using HrPayroll.Infrastructure.Import;

namespace HrPayroll.Infrastructure.Import;

/// <summary>يكتشف أعمدة ملف البصمة ويربطها بالحقول الداخلية.</summary>
public static class ColumnMapper
{
    private static readonly string[] NoAliases = { "no", "nо", "seq", "serial", "#", "م", "رقم" };
    private static readonly string[] DeviceAliases = { "deviceid", "device", "devicecode", "deviceno", "جهاز", "رقمالجهاز", "mai" };
    private static readonly string[] CodeAliases = { "id", "empid", "employeeid", "employeecode", "code", "userid", "user", "acno", "enrollid", "كود", "كودالموظف", "الرقمالوظيفي", "رقمالموظف" };
    private static readonly string[] NameAliases = { "name", "employeename", "username", "الاسم", "اسم", "اسمالموظف" };
    private static readonly string[] TypeAliases = { "atttype", "type", "statetype", "punchtype", "status", "نوع", "نوعالبصمة", "نوعالحركة", "الحالة" };
    private static readonly string[] TimeAliases = { "atttime", "time", "datetime", "punchtime", "attdatetime", "recordtime", "logtime", "التاريخالوقت", "الوقت", "التاريخ", "وقتالبصمة", "تاريخالوقت" };

    public static string Normalize(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;
        var sb = new System.Text.StringBuilder(header.Length);
        foreach (var ch in header.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch >= '\u0600') sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>يبحث عن صف العناوين ويبني الخريطة (يدعم وجود صفوف عنوان قبل العناوين).</summary>
    public static ColumnMapping? Detect(IReadOnlyList<string[]> rows, int maxScanRows = 15)
    {
        var limit = Math.Min(rows.Count, maxScanRows);
        ColumnMapping? best = null;
        var bestScore = -1;

        for (var r = 0; r < limit; r++)
        {
            var map = new ColumnMapping { HeaderRowIndex = r };
            var score = 0;
            for (var c = 0; c < rows[r].Length; c++)
            {
                var h = Normalize(rows[r][c]);
                if (h.Length == 0) continue;

                if (map.AttTime is null && TimeAliases.Any(a => h == a || h.Contains(a))) { map.AttTime = c; score += 3; continue; }
                // Device ID يُفحص قبل ID. لأن "deviceid" يحتوي على "id"
                if (map.DeviceId is null && DeviceAliases.Any(a => h == a || h.Contains(a))) { map.DeviceId = c; score += 2; continue; }
                if (map.EmployeeCode is null && CodeAliases.Any(a => h == a || h.Contains(a))) { map.EmployeeCode = c; score += 3; continue; }
                if (map.AttType is null && TypeAliases.Any(a => h == a || h.Contains(a))) { map.AttType = c; score += 2; continue; }
                if (map.Name is null && NameAliases.Any(a => h == a || h.Contains(a))) { map.Name = c; score += 1; continue; }
                if (map.No is null && NoAliases.Any(a => h == a || h.Contains(a))) { map.No = c; score += 1; continue; }
            }

            if (map.IsUsable && score > bestScore)
            {
                bestScore = score;
                best = map;
            }
        }

        return best;
    }

    /// <summary>خريطة احتياطية بالترتيب الافتراضي للأعمدة في تقرير الجهاز.</summary>
    public static ColumnMapping Fallback(IReadOnlyList<string[]> rows)
    {
        var header = rows.Count > 0 ? rows[0] : Array.Empty<string>();
        var map = new ColumnMapping { HeaderRowIndex = 0 };
        for (var c = 0; c < header.Length; c++)
        {
            if (map.No is null && c == 0) { map.No = c; continue; }
            if (map.DeviceId is null && c == 1) { map.DeviceId = c; continue; }
            if (map.EmployeeCode is null && c == 2) { map.EmployeeCode = c; continue; }
            if (map.Name is null && c == 3) { map.Name = c; continue; }
            if (map.AttType is null && c == 4) { map.AttType = c; continue; }
            if (map.AttTime is null && c == 5) { map.AttTime = c; continue; }
        }
        return map;
    }
}

/// <summary>يحوّل نص التاريخ/الوقت القادم من الجهاز إلى DateTime.</summary>
public static class PunchTimeParser
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm:ss", "d/M/yyyy H:mm",
        "MM/dd/yyyy HH:mm:ss", "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm tt", "MM/dd/yyyy hh:mm:ss tt",
        "dd-MM-yyyy HH:mm:ss", "d-M-yyyy H:mm", "yyyyMMddHHmmss", "yyyyMMdd HHmmss",
        "ddd MMM d HH:mm:ss yyyy", "M/d/yyyy H:mm", "dd.MM.yyyy HH:mm:ss"
    };

    private static readonly string[] Cultures = { "en-US", "en-GB", "ar-SA", "ar-EG", "fr-FR", "invariant" };

    public static bool TryParse(string? raw, out DateTime value, out string? error)
    {
        value = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "قيمة الوقت فارغة";
            return false;
        }

        var text = raw.Trim().Trim('"');

        // رقم تسلسلي لتاريخ Excel
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var oa)
            && oa > 1 && oa < 2958466)
        {
            try { value = DateTime.FromOADate(oa); return true; }
            catch (ArgumentException) { /* ليس تاريخ Excel */ }
        }

        foreach (var cultureName in Cultures)
        {
            var culture = cultureName == "invariant"
                ? System.Globalization.CultureInfo.InvariantCulture
                : System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            if (DateTime.TryParseExact(text, Formats, culture, System.Globalization.DateTimeStyles.None, out value))
                return true;
            if (DateTime.TryParse(text, culture, System.Globalization.DateTimeStyles.None, out value))
                return true;
        }

        error = "تعذّر تحويل التاريخ/الوقت: " + raw;
        return false;
    }
}
