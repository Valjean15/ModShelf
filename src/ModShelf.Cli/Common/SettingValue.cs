namespace ModShelf.Cli.Common;

/// <summary>
/// A setting as <c>settings show</c> lists it.
/// </summary>
/// <param name="Name">Its name, such as <c>library.root</c>.</param>
/// <param name="Value">Its value as text, the default's when it isn't set.</param>
/// <param name="IsDefault">Whether it isn't set, so the default applies.</param>
internal sealed record SettingValue(string Name, string Value, bool IsDefault);
