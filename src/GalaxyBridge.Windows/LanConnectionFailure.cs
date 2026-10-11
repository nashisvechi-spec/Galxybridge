using System.Net.Sockets;
using System.Security.Authentication;
namespace GalaxyBridge.Windows;

internal static class LanConnectionFailure
{
    public static string Describe(string stage, Exception error)
    {
        Exception detail = error.GetBaseException();
        int code = detail is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : detail.HResult;
        // Provider messages can contain addresses or sensitive data. Keep only
        // the local stage, exception type and numeric code in the diagnostic.
        string diagnostic = $"{stage}; {detail.GetType().Name}, 0x{code:X8}";
        if (error is AuthenticationException)
            return $"Не удалось проверить TLS. {diagnostic}. Если ошибка повторяется, пришлите эту строку.";
        if (error is IOException or SocketException or OperationCanceledException)
            return $"Попытка подключения прервана. {diagnostic}. Ожидаем автоматическое переподключение телефона. Если связь не восстановится, пришлите эту строку.";
        return $"Ошибка: {diagnostic}. Если ошибка повторяется, пришлите эту строку.";
    }
}
