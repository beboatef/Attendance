namespace HrPayroll.Domain.Enums;

/// <summary>حالة حضور اليوم للموظف.</summary>
public enum AttendanceStatus
{
    Present = 0,          // حاضر
    Absent = 1,           // غائب
    Late = 2,             // متأخر
    MissingCheckIn = 3,   // بصمة دخول مفقودة
    MissingCheckOut = 4,  // بصمة خروج مفقودة
    Leave = 5,            // إجازة
    RestDay = 6,          // يوم راحة
    Holiday = 7,          // عطلة رسمية
    NeedsReview = 8       // يحتاج مراجعة بشرية
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

/// <summary>حالة الاعتماد العامة.</summary>
public enum ApprovalStatus
{
    Pending = 0,   // قيد المراجعة
    Approved = 1,  // معتمد
    Rejected = 2   // مرفوض
}

/// <summary>حالة كشف الرواتب.</summary>
public enum PayrollStatus
{
    Draft = 0,     // مسودة
    Computed = 1,  // محسوب
    Approved = 2,  // معتمد (مقفل)
    Cancelled = 3  // ملغى
}

/// <summary>نوع الإجازة.</summary>
public enum LeaveType
{
    Annual = 0,    // سنوية
    Sick = 1,      // مرضية
    Unpaid = 2,    // بدون أجر
    Emergency = 3, // طارئة
    Other = 4
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
