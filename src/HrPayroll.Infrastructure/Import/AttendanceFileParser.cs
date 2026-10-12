using System.Globalization;
using System.Text;
using System.Xml;
using ExcelDataReader;

namespace HrPayroll.Infrastructure.Import;

/// <summary>
/// يكتشف نوع ملف البصمة من محتواه الفعلي ثم يستخرجه إلى صفوف نصية.
/// يعالج ملفات Excel الثنائية وملفات Excel 2003 XML التي تصدّرها أجهزة البصمة بامتداد .xls
/// بالإضافة إلى CSV وجداول HTML.
/// </summary>
public static class AttendanceFileParser
{
    static AttendanceFileParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>يكتشف نوع الملف من أول بايتاته.</summary>
    public static AttendanceFileFormat DetectFormat(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            return AttendanceFileFormat.Xlsx;

        if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
            return AttendanceFileFormat.Biff8;

        // تجاهل BOM والمسافات للتحقق من XML/HTML
        var offset = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) offset = 3;
        while (offset < bytes.Length && (bytes[offset] == (byte)' ' || bytes[offset] == (byte)'\r'
               || bytes[offset] == (byte)'\n' || bytes[offset] == (byte)'\t')) offset++;

        if (offset < bytes.Length && bytes[offset] == (byte)'<')
        {
            var head = Encoding.UTF8.GetString(bytes, offset, Math.Min(bytes.Length - offset, 4096)).ToLowerInvariant();
            if (head.Contains("urn:schemas-microsoft-com:office:spreadsheet"))
                return AttendanceFileFormat.SpreadsheetXml;
            if (head.Contains("<html") || head.Contains("<table"))
                return AttendanceFileFormat.HtmlTable;
        }

        return AttendanceFileFormat.Csv;
    }

    /// <summary>يستخرج الصفوف من الملف.</summary>
    public static ParsedSheet Parse(byte[] bytes, AttendanceFileFormat format)
    {
        return format switch
        {
            AttendanceFileFormat.Xlsx or AttendanceFileFormat.Biff8 => ParseWithExcelDataReader(bytes, format),
            AttendanceFileFormat.SpreadsheetXml => ParseSpreadsheetXml(bytes),
            AttendanceFileFormat.HtmlTable => ParseHtmlTable(bytes),
            _ => ParseCsv(bytes)
        };
    }

    private static ParsedSheet ParseWithExcelDataReader(byte[] bytes, AttendanceFileFormat format)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(ms);
        var rows = new List<string[]>();
        var max = 0;
        do
        {
            while (reader.Read())
            {
                var arr = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    arr[i] = FormatCell(reader.GetValue(i));
                max = Math.Max(max, arr.Length);
                rows.Add(arr);
            }
        } while (reader.NextResult());

        return new ParsedSheet { Format = format, Rows = rows, ColumnCount = max };
    }

    private static string FormatCell(object? value)
    {
        if (value is null) return string.Empty;
        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static ParsedSheet ParseSpreadsheetXml(byte[] bytes)
    {
        var rows = new List<string[]>();
        var max = 0;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true };
        using var ms = new MemoryStream(bytes);
        using var reader = XmlReader.Create(ms, settings);

        var currentRow = new List<string>();
        var inCell = false;
        var cellIsString = false;
        var inData = false;
        var cellBuffer = new StringBuilder();
        string? cellIndex = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "Row":
                        currentRow = new List<string>();
                        break;
                    case "Cell":
                        inCell = true;
                        cellBuffer.Clear();
                        cellIndex = reader.GetAttribute("Index");
                        var type = reader.GetAttribute("Type");
                        cellIsString = string.Equals(type, "String", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "Data":
                        inData = true;
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.Text && inData && inCell)
            {
                cellBuffer.Append(reader.Value);
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "Data":
                        inData = false;
                        break;
                    case "Cell":
                        inCell = false;
                        // احترام فهارس الخلايا (Index) لملء الفراغات
                        if (cellIndex is not null && int.TryParse(cellIndex, out var idx1))
                        {
                            while (currentRow.Count < idx1 - 1) currentRow.Add(string.Empty);
                        }
                        currentRow.Add(cellBuffer.ToString());
                        cellIndex = null;
                        break;
                    case "Row":
                        max = Math.Max(max, currentRow.Count);
                        rows.Add(currentRow.ToArray());
                        break;
                }
            }
        }

        return new ParsedSheet { Format = AttendanceFileFormat.SpreadsheetXml, Rows = rows, ColumnCount = max };
    }

    private static ParsedSheet ParseHtmlTable(byte[] bytes)
    {
        var text = DecodeText(bytes);
        var rows = new List<string[]>();
        var max = 0;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true };
        try
        {
            using var stringReader = new StringReader(text);
            using var reader = XmlReader.Create(stringReader, settings);
            var row = new List<string>();
            var buffer = new StringBuilder();
            var inCell = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.LocalName == "tr") { row = new List<string>(); }
                    else if (reader.LocalName is "td" or "th") { inCell = true; buffer.Clear(); }
                }
                else if (reader.NodeType == XmlNodeType.Text && inCell) buffer.Append(reader.Value);
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.LocalName is "td" or "th") { inCell = false; row.Add(buffer.ToString().Trim()); }
                    else if (reader.LocalName == "tr")
                    {
                        if (row.Count > 0) { max = Math.Max(max, row.Count); rows.Add(row.ToArray()); }
                    }
                }
            }
        }
        catch
        {
            // احتياطي: تقسيم بسيط
            foreach (var line in text.Split('\n'))
            {
                if (line.Contains('<')) continue;
                var cells = line.Split('\t');
                if (cells.Length > 1) { max = Math.Max(max, cells.Length); rows.Add(cells); }
            }
        }
        return new ParsedSheet { Format = AttendanceFileFormat.HtmlTable, Rows = rows, ColumnCount = max };
    }

    private static ParsedSheet ParseCsv(byte[] bytes)
    {
        var text = DecodeText(bytes);
        var rows = new List<string[]>();
        var max = 0;
        foreach (var line in SplitCsv(text))
        {
            if (line.Length == 1 && string.IsNullOrWhiteSpace(line[0])) continue;
            max = Math.Max(max, line.Length);
            rows.Add(line);
        }
        return new ParsedSheet { Format = AttendanceFileFormat.Csv, Rows = rows, ColumnCount = max };
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);
        // جرّب UTF-8 فإن فشل استخدم نافذة عربية (كثير من أجهزة البصمة تصدّر windows-1256)
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1256).GetString(bytes);
        }
    }

    /// <summary>تقسيم CSV مع دعم علامات التنصيص.</summary>
    private static IEnumerable<string[]> SplitCsv(string text)
    {
        var current = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        var delim = text.Contains('\t') && !text.Contains(',') ? '\t'
            : (text.Contains(';') && !text.Contains(',')) ? ';' : ',';

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == delim) { current.Add(sb.ToString().Trim()); sb.Clear(); }
                else if (c == '\n') { current.Add(sb.ToString().Trim()); sb.Clear(); yield return current.ToArray(); current = new List<string>(); }
                else if (c == '\r') { }
                else sb.Append(c);
            }
        }
        if (sb.Length > 0 || current.Count > 0)
        {
            current.Add(sb.ToString().Trim());
            yield return current.ToArray();
        }
    }
}
