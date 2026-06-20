using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public enum NotificationType
{
    Info,
    Error
}

public static class Notification
{
    private static readonly Lock Lock = new();
    private static string _message = "";
    private static NotificationType _type = NotificationType.Info;
    private static DateTime _expiresAt = DateTime.MinValue;

    public static void Show(string message, NotificationType type = NotificationType.Info)
    {
        lock (Lock)
        {
            _message = message;
            _type = type;
            _expiresAt = DateTime.UtcNow.AddSeconds(5);
        }
    }

    public static void Clear()
    {
        lock (Lock)
        {
            _message = "";
            _expiresAt = DateTime.MinValue;
        }
    }

    public static bool HasActiveNotification
    {
        get
        {
            lock (Lock) return !string.IsNullOrEmpty(_message) && DateTime.UtcNow <= _expiresAt;
        }
    }

    public static IRenderable? GetRenderable()
    {
        lock (Lock)
        {
            if (string.IsNullOrEmpty(_message) || DateTime.UtcNow > _expiresAt)
                return null;

            var borderColor = _type == NotificationType.Error ? Color.Red : Color.Blue;
            var markup = new Markup($" {_message} ");

            return new Panel(markup)
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(borderColor),
                Padding = new Padding(0, 0, 0, 0),
                Width = Math.Min(_message.Length + 4, 50)
            };
        }
    }
}
