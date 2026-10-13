using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace HrPayroll.Web.Controllers;

/// <summary>أدوات مساعدة لقراءة المستخدم الحالي واكتشاف طلبات AJAX.</summary>
public static class ControllerExtensions
{
    public static string? CurrentUserName(this ControllerBase controller) => controller.User.Identity?.Name;

    public static string? CurrentUserId(this ControllerBase controller)
        => controller.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? ClientIp(this ControllerBase controller)
        => controller.HttpContext.Connection.RemoteIpAddress?.ToString();

    public static bool IsAjax(this ControllerBase controller)
        => string.Equals(controller.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
