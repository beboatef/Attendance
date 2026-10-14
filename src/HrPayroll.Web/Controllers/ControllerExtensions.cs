using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Web.Controllers;

/// <summary>أدوات مساعدة لقراءة المستخدم الحالي واكتشاف طلبات AJAX.</summary>
public static class ControllerExtensions
{
    /// <summary>الرسالة العامة التي تُعرض للمستخدم بدل تفاصيل أي خطأ داخلي.</summary>
    public const string GenericErrorMessage =
        "تعذّر تنفيذ العملية بسبب خطأ غير متوقع. تم تسجيل التفاصيل في سجل النظام، ويمكنك المحاولة مرة أخرى.";

    public static string? CurrentUserName(this ControllerBase controller) => controller.User.Identity?.Name;

    public static string? CurrentUserId(this ControllerBase controller)
        => controller.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? ClientIp(this ControllerBase controller)
        => controller.HttpContext.Connection.RemoteIpAddress?.ToString();

    public static bool IsAjax(this ControllerBase controller)
        => string.Equals(controller.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// رسالة الخطأ الآمنة المعروضة للمستخدم:
    /// تُعرض رسائل قواعد العمل العربية (InvalidOperationException) كما هي،
    /// وتُستبدل أي أخطاء داخلية (قاعدة بيانات، شبكة، أخطاء برمجية) برسالة عامة
    /// مع تسجيل التفاصيل الكاملة في سجل الخادم فقط.
    /// </summary>
    public static string UserMessage(this Exception ex, ILogger? logger = null, string? context = null)
    {
        if (ex is InvalidOperationException) return ex.Message;
        logger?.LogError(ex, "{Context}", context ?? "خطأ غير متوقع أثناء تنفيذ عملية في الواجهة");
        return GenericErrorMessage;
    }
}
