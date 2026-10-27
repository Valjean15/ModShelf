namespace ModShelf.Application.Notifications.Ports;

/// <summary>
/// A notice for the user, such as the new updates a background check found.
/// </summary>
/// <param name="Title">Its first line, in bold where the system has bold.</param>
/// <param name="Message">What it says under the title.</param>
public sealed record Notification(string Title, string Message);
