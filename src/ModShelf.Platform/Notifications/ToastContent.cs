using System.Xml.Linq;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// A notice as a Windows toast's XML: the generic template, with the title and the message as its two lines of text.
/// </summary>
internal static class ToastContent
{
    public static string For(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        XElement toast = new(
            "toast",
            new XElement(
                "visual",
                new XElement(
                    "binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", notification.Title),
                    new XElement("text", notification.Message))));
        return toast.ToString(SaveOptions.DisableFormatting);
    }
}
