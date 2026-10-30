using System.Diagnostics.CodeAnalysis;

using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// A link to a mod or one of its files, as a command's argument.
/// </summary>
internal static class ModLinkArgument
{
    /// <summary>
    /// Reads <paramref name="text"/> as a complete link; when it isn't one, says so.
    /// </summary>
    public static bool TryRead(CliEnvironment environment, string text, [NotNullWhen(true)] out Uri? link)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (Uri.TryCreate(text, UriKind.Absolute, out link))
        {
            return true;
        }

        environment.Error.WriteLine("That isn't a complete link, such as https://www.nexusmods.com/stardewvalley/mods/2400.");
        return false;
    }
}
