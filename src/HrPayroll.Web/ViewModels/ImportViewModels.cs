namespace HrPayroll.Web.ViewModels;

/// <summary>معاينة استيراد موظفين مع إمكانية تعديل ربط الأعمدة قبل التأكيد.</summary>
public class EmployeeImportPreviewViewModel
{
    public HrPayroll.Infrastructure.Import.EmployeeImportAnalysis Analysis { get; set; } = new();
    public string Token { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool HasMapping => Analysis.Mapping is not null && Analysis.Mapping.IsUsable;
    public List<string> DepartmentNames { get; set; } = new();
}

/// <summary>
/// منطقة رفع ملف واحد (الدخول أو الخروج): تُعرض بشكل مستقل حتى لا يختلط الملفان على المستخدم،
/// وتحمل حالة الملف ونتيجة التحقق منه وفترة البيانات المكتشفة داخله.
/// </summary>
public class ImportFileZoneViewModel
{
    /// <summary>معرّف المنطقة: entry أو exit.</summary>
    public string Key { get; set; } = "entry";

    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;

    /// <summary>اسم حقل الملف في النموذج (entryFile / exitFile).</summary>
    public string InputName { get; set; } = "entryFile";

    /// <summary>هل اختار المستخدم ملفًا في هذه المنطقة؟</summary>
    public bool Provided { get; set; }

    public string? FileName { get; set; }
    public string? Token { get; set; }

    /// <summary>رسالة خطأ عربية واضحة إن فشل التحقق من هذا الملف تحديدًا.</summary>
    public string? Error { get; set; }

    public HrPayroll.Infrastructure.Import.ImportAnalysisResult? Analysis { get; set; }

    public string FormatName => Analysis is null ? "-" : FormatDisplay(Analysis.Format);

    /// <summary>الملف صالح وقابل للاستيراد.</summary>
    public bool Ok => Provided && Error is null && Analysis is { HasMapping: true };

    public int Total => Analysis?.Total ?? 0;
    public int Accepted => Analysis?.SuccessCount ?? 0;
    public int Duplicates => Analysis?.DuplicateCount ?? 0;
    public int AlreadyImported => Analysis?.AlreadyImportedCount ?? 0;
    public int Invalid => Analysis?.InvalidCount ?? 0;
    public int UnknownEmployees => Analysis?.UnknownEmployeeCount ?? 0;

    /// <summary>سجلات تحتاج إلى مراجعة بشرية: كود غير معروف أو تاريخ/وقت غير صالح.</summary>
    public int NeedsReview => Invalid + UnknownEmployees;

    private (DateTime? From, DateTime? To) Period
    {
        get
        {
            var times = Analysis?.Rows.Where(r => r.PunchTime.HasValue).Select(r => r.PunchTime!.Value).ToList();
            if (times is null || times.Count == 0) return (null, null);
            return (times.Min(), times.Max());
        }
    }

    public DateTime? PeriodFrom => Period.From;
    public DateTime? PeriodTo => Period.To;

    /// <summary>فترة البيانات المستخلصة من الملف (تاريخ الملف).</summary>
    public string PeriodText
    {
        get
        {
            var (from, to) = Period;
            if (from is null || to is null) return "لم يتم استخلاص فترة بيانات من الملف";
            return from.Value.Date == to.Value.Date
                ? $"فترة البيانات: {from:yyyy-MM-dd}"
                : $"فترة البيانات: من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        }
    }

    /// <summary>حالة الملف كما تُعرض للمستخدم بالعربية.</summary>
    public string StatusText
    {
        get
        {
            if (!Provided) return "لم يتم الاختيار";
            if (Error is not null) return "فشل التحقق من الملف";
            if (Analysis is null) return "لم يتم تحليل الملف";
            if (!Analysis.HasMapping) return "ملف غير صالح — تعذّر التعرف على الأعمدة المطلوبة";
            if (Accepted == 0 && NeedsReview == 0 && Duplicates > 0) return "لا توجد سجلات جديدة (مستوردة مسبقًا)";
            return "جاهز للرفع والاستيراد";
        }
    }

    /// <summary>هل يحمل الملف سجلات جديدة فعلًا (لا كلها مكررة)؟</summary>
    public bool HasNewRecords => Ok && Accepted > 0;

    public string StatusClass
    {
        get
        {
            if (!Provided) return "idle";
            if (Error is not null || Analysis is not { HasMapping: true }) return "bad";
            if (!HasNewRecords) return "warn";
            return "ok";
        }
    }

    private static string FormatDisplay(HrPayroll.Infrastructure.Import.AttendanceFileFormat f) => f switch
    {
        HrPayroll.Infrastructure.Import.AttendanceFileFormat.Xlsx => "Excel حديث (xlsx)",
        HrPayroll.Infrastructure.Import.AttendanceFileFormat.Biff8 => "Excel ثنائي قديم (xls)",
        HrPayroll.Infrastructure.Import.AttendanceFileFormat.SpreadsheetXml => "Excel 2003 XML",
        HrPayroll.Infrastructure.Import.AttendanceFileFormat.HtmlTable => "جدول HTML",
        HrPayroll.Infrastructure.Import.AttendanceFileFormat.Csv => "ملف نصي/CSV",
        _ => "غير معروف"
    };
}

/// <summary>صفحة رفع الملفين ثم معاينتهما معًا قبل تأكيد الاستيراد والمعالجة.</summary>
public class ImportFilesViewModel
{
    public List<ImportFileZoneViewModel> Zones { get; set; } = new();
    public List<string> Headers { get; set; } = new();

    public ImportFileZoneViewModel? Entry => Zones.FirstOrDefault(z => z.Key == "entry");
    public ImportFileZoneViewModel? Exit => Zones.FirstOrDefault(z => z.Key == "exit");

    /// <summary>عدد الملفات الصالحة القابلة للتأكيد.</summary>
    public int ValidFiles => Zones.Count(z => z.Ok);

    public int TotalAccepted => Zones.Where(z => z.Ok).Sum(z => z.Accepted);
    public int TotalNeedsReview => Zones.Where(z => z.Ok).Sum(z => z.NeedsReview);
    public int TotalDuplicates => Zones.Where(z => z.Ok).Sum(z => z.Duplicates);

    /// <summary>عدد الملفات التي تحمل سجلات جديدة فعلًا.</summary>
    public int FilesWithNewRecords => Zones.Count(z => z.HasNewRecords);

    /// <summary>
    /// هل يمكن تأكيد الاستيراد؟ لا بد من ملف صالح واحد على الأقل يحمل بصمة جديدة،
    /// فلا يُسمح بإعادة استيراد ملفات كل سجلاتها مستوردة مسبقًا.
    /// </summary>
    public bool CanConfirm => FilesWithNewRecords > 0;
}
