using System.Runtime.CompilerServices;

namespace ModShelf.Platform.Secrets;

/// <summary>
/// The rules for secret names, shared by every store: lowercase ASCII letters, digits, '-' and '.', in parts
/// separated by '/', such as <c>account/nexus</c>.
/// </summary>
/// <remarks>
/// Names this narrow work unchanged as Keychain accounts and Secret Service attributes, and map one to one onto
/// file names on every system ('/' becomes '_', which names can't contain).
/// </remarks>
internal static class SecretNames
{
    public const int MaxLength = 128;

    /// <summary>
    /// Throws unless <paramref name="name"/> follows the rules.
    /// </summary>
    public static void Validate(string name, [CallerArgumentExpression(nameof(name))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, paramName);
        if (name.Length > MaxLength || !name.Split('/').All(IsValidPart))
        {
            throw new ArgumentException(
                $"'{name}' isn't a valid secret name. Use lowercase letters, digits, '-' and '.', in parts separated by '/', such as \"account/nexus\".",
                paramName);
        }
    }

    /// <summary>
    /// The file name for the secret, for the stores that keep one file per secret.
    /// </summary>
    public static string ToFileName(string name) => $"{name.Replace('/', '_')}.secret";

    private static bool IsValidPart(string part)
        => part.Length > 0
            && part is not ("." or "..")
            && part.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.');
}
