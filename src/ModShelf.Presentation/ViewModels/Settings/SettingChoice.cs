namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A value a setting can take, as a list on the Settings screen offers it.
/// </summary>
public sealed record SettingChoice<T>
{
    internal SettingChoice(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    /// <summary>
    /// Such as "512 MB (the default)".
    /// </summary>
    public string Label { get; }

    public override string ToString() => Label;
}
