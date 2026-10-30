using ModShelf.Application.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Sources;

/// <summary>
/// Finding which source a link belongs to, and the source that can do what a use case asks.
/// </summary>
internal static class SourceRegistryExtensions
{
    /// <summary>
    /// The file a link the user gives is to, as the source that knows it says.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The link isn't absolute, or the source that knows it finds it malformed, or not to a mod or a file.
    /// </exception>
    /// <exception cref="UnsupportedLinkException">No source knows the link; why is the inner exception.</exception>
    public static async Task<RecognizedLink> RecognizeLinkAsync(this ISourceRegistry sources, Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            throw new ArgumentException("The link must be absolute.", nameof(link));
        }

        try
        {
            return await sources.RecognizeAsync(link, cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedLinkException exception)
        {
            throw new UnsupportedLinkException("No source knows this link.", exception);
        }
    }

    /// <summary>
    /// The file a link the user gives is to, as <see cref="RecognizeLinkAsync"/> finds it, and its source's
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <param name="unable">What to say when the link's source isn't a <typeparamref name="T"/>.</param>
    /// <exception cref="UnsupportedLinkException">
    /// No source knows the link, or the one that does isn't a <typeparamref name="T"/>, which says
    /// <paramref name="unable"/>.
    /// </exception>
    public static async Task<(RecognizedLink Link, T Source)> RecognizeForAsync<T>(
        this ISourceRegistry sources,
        Uri link,
        string unable,
        CancellationToken cancellationToken)
        where T : class, IModSource
    {
        RecognizedLink recognized = await sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false);
        T source = sources.Find<T>(recognized.File.Mod.Game.Source) ?? throw new UnsupportedLinkException(unable);
        return (recognized, source);
    }

    /// <summary>
    /// The <typeparamref name="T"/> of a source a caller names, such as a mod's or a game's.
    /// </summary>
    /// <param name="cannot">What the source can't do unless it's a <typeparamref name="T"/>, such as "can't search".</param>
    /// <param name="paramName">The caller's parameter that named the source.</param>
    /// <exception cref="ArgumentException">The source isn't registered, or isn't a <typeparamref name="T"/>.</exception>
    public static T Require<T>(this ISourceRegistry sources, SourceId source, string cannot, string paramName)
        where T : class, IModSource
        => sources.Find<T>(source) ?? throw new ArgumentException($"Source '{source}' isn't registered, or {cannot}.", paramName);

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
