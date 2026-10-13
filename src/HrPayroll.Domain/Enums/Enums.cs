namespace HrPayroll.Domain.Enums;

/// <summary>
/// حالة حضور اليوم للموظف.
/// ملاحظة: القيم الجديدة تُضاف في نهاية الترقيم حتى لا تتغير القيم المحفوظة مسبقًا في قاعدة البيانات.
/// </summary>
public enum AttendanceStatus
{
    Present = 0,          // حاضر
    Absent = 1,           // غائب (بدون تصنيف إجازة)
    Late = 2,             // متأخر
    MissingCheckIn = 3,   // بصمة دخول مفقودة
    MissingCheckOut = 4,  // بصمة خروج مفقودة
    Leave = 5,            // إجازة معتمدة (عام)
    RestDay = 6,          // يوم راحة أسبوعية
    Holiday = 7,          // عطلة رسمية
    NeedsReview = 8,      // يحتاج مراجعة بشرية

    EarlyDeparture = 9,      // انصراف مبكر
    PaidLeave = 10,          // إجازة مدفوعة
    UnpaidLeave = 11,        // إجازة بدون أجر
    UnexcusedAbsence = 12    // غياب بدون عذر
}

/// <summary>اتجاه حركة البصمة.</summary>
public enum PunchDirection
{
    Unknown = 0, // غير معروف (مثال: نوع البصمة FP لا يحدد الاتجاه)
    In = 1,      // دخول
    Out = 2      // خروج
}

/// <summary>نوع الحدث الحضوري المصنف.</summary>
public enum AttendanceEventType
{
    Unknown = 0,
    Entry = 1,
    Exit = 2
}

/// <summary>مصدر التصنيف.</summary>
public enum ClassificationSource
{
    None = 0,
    ExplicitDeviceType = 1, // نوع الحركة القادم صراحة من الجهاز
    Rule = 2,               // قواعد آلية
    Manual = 3              // مراجعة يدوية
}

/// <summary>حالة تصنيف/مراجعة البصمات.</summary>
public enum ClassificationStatus
{
    Unclassified = 0,  // غير مصنف
    NeedsReview = 1,   // يحتاج مراجعة
    Classified = 2     // مصنف بثقة
}

/// <summary>مصدر تحديد وردية اليوم.</summary>
public enum ShiftDetectionSource
{
    None = 0,       // لم تُحدَّد وردية
    Assigned = 1,   // من الإسناد الصريح للموظف
    Detected = 2,   // بالكشف الآلي من البصمات
    Ambiguous = 3   // غامض: يحتاج مراجعة
}

/// <summary>حالة الاعتماد العامة.</summary>
public enum ApprovalStatus
{
    Pending = 0,   // قيد المراجعة
    Approved = 1,  // معتمد
    Rejected = 2   // مرفوض
}

/// <summary>حالة كشف الرواتب. القيم الجديدة تُضاف في النهاية للحفاظ على القيم المحفوظة.</summary>
public enum PayrollStatus
{
    Draft = 0,          // مسودة
    Computed = 1,       // محسوب
    Approved = 2,       // معتمد
    Cancelled = 3,      // ملغى
    PendingReview = 4,  // قيد المراجعة
    Locked = 5,         // مقفل
    Reopened = 6        // أُعيد فتحه للتصحيح
}

/// <summary>نوع الإجازة / تصنيف اليوم.</summary>
public enum LeaveType
{
    Annual = 0,             // سنوية
    Sick = 1,               // مرضية
    Unpaid = 2,             // بدون أجر
    Emergency = 3,          // طارئة
    Other = 4,              // أخرى
    PublicHoliday = 5,      // عطلة رسمية
    UnexcusedAbsence = 6,   // غياب بدون عذر
    Paid = 7,               // إجازة مدفوعة (عام)
    WeeklyRest = 8          // راحة أسبوعية
}

/// <summary>حالة الموظف الوظيفية.</summary>
public enum EmploymentStatus
{
    Active = 0,      // على العمل
    OnLeave = 1,     // في إجازة طويلة
    Suspended = 2,   // موقوف
    Terminated = 3   // منتهي الخدمة
}

/// <summary>نوع التسوية المالية (مكافأة/بدل/خصم آخر/أيام إضافية).</summary>
public enum AdjustmentType
{
    Bonus = 0,          // مكافأة
    Allowance = 1,      // بدل
    OtherDeduction = 2, // خصم آخر
    AdditionalDays = 3  // أيام عمل إضافية مستحقة خارج فترة الراتب
}

/// <summary>نتيجة صف الاستيراد.</summary>
public enum ImportRowStatus
{
    Success = 0,            // ناجح
    Duplicate = 1,          // مكرر
    InvalidDateTime = 2,    // تاريخ/وقت غير صالح
    UnknownEmployee = 3,    // موظف غير مسجل
    InvalidRow = 4          // صف غير صالح
}

/// <summary>الوردية المستخدمة لحساب الرواتب (سياسة).</summary>
public enum PayrollDayBasis
{
    Fixed30Days = 0,     // 30 يوم × 8 ساعات = 240 ساعة
    ActualWorkDays = 1   // أيام العمل الفعلية × ساعات الوردية
}

/// <summary>حالة صف استيراد الموظفين.</summary>
public enum EmployeeImportRowStatus
{
    New = 0,        // موظف جديد سيُضاف
    Update = 1,     // موظف موجود سيُحدَّث
    Unchanged = 2,  // موجود بدون تغيير
    Duplicate = 3,  // مكرر داخل الملف أو مكرر كود
    Invalid = 4     // بيانات غير صالحة
}

/// <summary>وضع استيراد الموظفين: إضافة الجديد فقط أو تحديث الموجود أيضًا.</summary>
public enum EmployeeImportMode
{
    CreateOnly = 0,      // إضافة الموظفين الجدد فقط
    CreateAndUpdate = 1  // إضافة الجدد وتحديث الموجودين صراحةً
}
