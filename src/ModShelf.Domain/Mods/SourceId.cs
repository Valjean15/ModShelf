namespace ModShelf.Domain.Mods;

/// <summary>
/// Identifies a mod source, such as <c>nexus</c> or <c>mod-io</c>.
/// </summary>
/// <remarks>
/// Source IDs are stable keys: they are stored in the library database and can become part of folder
/// names. They are therefore limited to lowercase ASCII letters, digits and inner hyphens, so they stay
/// identical on case-sensitive and case-insensitive file systems.
/// </remarks>
public sealed record SourceId
{
    public SourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid source ID. Use lowercase letters, digits and inner hyphens, such as \"nexus\" or \"mod-io\".",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        if (value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
