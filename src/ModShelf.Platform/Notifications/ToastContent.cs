using System.Xml.Linq;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// A notice as a Windows toast's XML: the generic template, with the title and the message as its two lines of text.
/// </summary>
internal static class ToastContent
{
    /// <param name="link">
    /// The link a click on the toast opens, through the app the system opens its scheme with; null for none, and a
    /// click only closes it.
    /// </param>
    public static string For(Notification notification, string? link = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        XElement toast = new(
            "toast",
            link is null ? null : new XAttribute("launch", link),
            link is null ? null : new XAttribute("activationType", "protocol"),
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
