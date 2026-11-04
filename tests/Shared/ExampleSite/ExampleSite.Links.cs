using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Testing;

public sealed partial class ExampleSite
{
    /// <summary>
    /// A mod's link is to its newest file, which the site is asked for: the link alone doesn't say which.
    /// </summary>
    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri
            || (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp)
            || !string.Equals(link.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<RecognizedLink?>(null);
        }

        string[] parts = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool isMod = parts.Length == 3 && parts[1] == "mod";
        bool isFile = parts.Length == 5 && parts[1] == "mod" && parts[3] == "file";
        if (!isMod && !isFile)
        {
            throw new ArgumentException("That's a page of Example Mods, but not a mod's or a file's.", nameof(link));
        }

        ModRef mod = new(new GameRef(SourceId, parts[0]), parts[2]);
        ModFileRef file = isFile ? new ModFileRef(mod, parts[4]) : NewestMainFile(ModNamed(mod)).Reference;
        return Task.FromResult<RecognizedLink?>(new RecognizedLink(file));
    }
}
