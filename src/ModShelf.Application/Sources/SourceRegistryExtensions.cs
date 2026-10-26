using ModShelf.Application.Downloads;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Sources;

/// <summary>
/// Finding which source a link belongs to.
/// </summary>
internal static class SourceRegistryExtensions
{
    /// <summary>
    /// Asks every <see cref="ILinkRecognizer"/> about <paramref name="link"/>. A site-specific match wins over a
    /// catch-all one.
    /// </summary>
    /// <exception cref="UnsupportedLinkException">No source recognizes the link.</exception>
    /// <exception cref="InvalidOperationException">More than one site claims the link.</exception>
    public static async Task<RecognizedLink> RecognizeAsync(this ISourceRegistry sources, Uri link, CancellationToken cancellationToken)
    {
        List<(ILinkRecognizer Source, RecognizedLink Match)> matches = [];
        foreach (ILinkRecognizer source in sources.All<ILinkRecognizer>())
        {
            RecognizedLink? match = await source.RecognizeAsync(link, cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                matches.Add((source, match));
            }
        }

        string[] specific = [.. matches.Where(found => !found.Match.IsCatchAll).Select(found => found.Source.Id.Value)];
        if (specific.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one source claims this link ({string.Join(", ", specific)}), so it's ambiguous.");
        }

        return matches.Select(found => found.Match).OrderBy(match => match.IsCatchAll).FirstOrDefault()
            ?? throw new UnsupportedLinkException();
    }
}
