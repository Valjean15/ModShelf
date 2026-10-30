using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Links;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>links status</c>, <c>links take [site]</c> and <c>links give-back [site]</c>: which app the system opens the links
/// the sites hand their downloads over with, such as Nexus Mods' <c>nxm://</c> links, and making the desktop app open
/// them, or giving them back.
/// </summary>
internal static class LinksCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command status = new("status", "Shows which app opens each site's links.");
        status.SetAction((_, cancellationToken) => StatusAsync(environment, cancellationToken));

        Argument<string?> takeSite = Site();
        Command take = new("take", "Makes the system open a site's links with the desktop app, remembering the app that opened them before.") { takeSite };
        take.SetAction((parsed, cancellationToken) => ChangeAsync(environment, parsed.GetValue(takeSite), giveBack: false, cancellationToken));

        Argument<string?> giveBackSite = Site();
        Command giveBack = new("give-back", "Stops the system opening a site's links with ModShelf, and gives them back to the app that opened them before.") { giveBackSite };
        giveBack.SetAction((parsed, cancellationToken) => ChangeAsync(environment, parsed.GetValue(giveBackSite), giveBack: true, cancellationToken));

        return new Command("links", "Shows and changes which app opens the links sites hand their downloads over with, such as Nexus Mods' nxm:// links from its \"mod manager download\" buttons. The desktop app opens them when ModShelf takes them.")
        {
            status,
            take,
            giveBack,
        };
    }

    private static Argument<string?> Site() => new("site")
    {
        Description = "The site, such as nexus; it can be left out when only one site hands its downloads over with links.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private static Task<int> StatusAsync(CliEnvironment environment, CancellationToken cancellationToken)
        => CommandHost.RunAsync(
            environment,
            async services =>
            {
                IReadOnlyList<LinkHandler> handlers;
                try
                {
                    handlers = await services.GetRequiredService<ILinkHandlers>().ListAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await environment.Error.WriteLineAsync($"Which app opens links couldn't be read: {exception.Message}").ConfigureAwait(false);
                    return ExitCodes.Failed;
                }

                if (handlers.Count == 0)
                {
                    environment.Output.WriteLine("No site hands its downloads over with links.");
                    return ExitCodes.Done;
                }

                TextTable.Write(
                    environment.Output,
                    [["Site", "Links", "Opened by"], .. handlers.Select(handler => new[] { handler.Source.Value, $"{handler.Scheme}://", OpenedBy(handler.State) })]);
                if (handlers.Any(handler => handler.State.Kind == SchemeHandlerKind.Unavailable))
                {
                    environment.Output.WriteLine(
                        "Where it says unavailable, ModShelf can't take the links here: on macOS only the installed app can, and elsewhere it needs "
                        + "the desktop app, beside the CLI or where ModShelf__DesktopApp says.");
                }

                environment.Output.WriteLine("To make the desktop app open a site's links: links take <site>");
                return ExitCodes.Done;
            },
            cancellationToken);

    private static Task<int> ChangeAsync(CliEnvironment environment, string? site, bool giveBack, CancellationToken cancellationToken)
        => CommandHost.RunAsync(
            environment,
            async services =>
            {
                ILinkHandlers handlers = services.GetRequiredService<ILinkHandlers>();
                SourceId? source = site is null ? await TheOnlySiteAsync(handlers, cancellationToken).ConfigureAwait(false) : new SourceId(site);
                if (source is null)
                {
                    await environment.Error.WriteLineAsync("Name the site, such as nexus: links status lists them.").ConfigureAwait(false);
                    return ExitCodes.BadInput;
                }

                LinkHandler changed;
                try
                {
                    changed = giveBack
                        ? await handlers.GiveBackAsync(source, cancellationToken).ConfigureAwait(false)
                        : await handlers.TakeAsync(source, cancellationToken).ConfigureAwait(false);
                }
                catch (ArgumentException)
                {
                    await environment.Error.WriteLineAsync($"No site '{site}' hands its downloads over with links: links status lists them.").ConfigureAwait(false);
                    return ExitCodes.BadInput;
                }
                catch (InvalidOperationException exception)
                {
                    await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return ExitCodes.Failed;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await environment.Error.WriteLineAsync($"The system's apps for links couldn't be changed: {exception.Message}").ConfigureAwait(false);
                    return ExitCodes.Failed;
                }

                return giveBack ? await GaveBackAsync(environment, changed).ConfigureAwait(false) : await TookAsync(environment, changed).ConfigureAwait(false);
            },
            cancellationToken);

    private static async Task<int> TookAsync(CliEnvironment environment, LinkHandler handler)
    {
        string links = $"{handler.Scheme}:// links from {handler.SiteName}";
        if (handler.State.Kind != SchemeHandlerKind.ModShelf)
        {
            await environment.Error.WriteLineAsync(
                    $"{handler.State.OtherApp ?? "Another app"} still opens {links}: it was chosen in the system's own settings. "
                    + $"Choose ModShelf for {handler.Scheme}:// links there.")
                .ConfigureAwait(false);
            return ExitCodes.Failed;
        }

        environment.Output.WriteLine(
            handler.State.PreviousApp is { } previous
                ? $"The desktop app opens {links} now. {previous} opened them before; links give-back {handler.Source} gives them back to it."
                : $"The desktop app opens {links} now. links give-back {handler.Source} stops it.");
        return ExitCodes.Done;
    }

    private static async Task<int> GaveBackAsync(CliEnvironment environment, LinkHandler handler)
    {
        if (handler.State.Kind == SchemeHandlerKind.ModShelf)
        {
            await environment.Error.WriteLineAsync($"No other app opens {handler.Scheme}:// links, so the system still opens them with ModShelf.").ConfigureAwait(false);
            return ExitCodes.Failed;
        }

        environment.Output.WriteLine($"ModShelf no longer opens {handler.Scheme}:// links from {handler.SiteName}. {OpenedBy(handler.State, sentence: true)}");
        return ExitCodes.Done;
    }

    /// <summary>
    /// The only site that hands its downloads over with links; null when there are several, or none.
    /// </summary>
    private static async Task<SourceId?> TheOnlySiteAsync(ILinkHandlers handlers, CancellationToken cancellationToken)
        => await handlers.ListAsync(cancellationToken).ConfigureAwait(false) is [{ } only] ? only.Source : null;

    private static string OpenedBy(SchemeState state, bool sentence = false) => (state, sentence) switch
    {
        ({ Kind: SchemeHandlerKind.ModShelf, PreviousApp: { } previous }, _) => $"ModShelf (before it, {previous})",
        ({ Kind: SchemeHandlerKind.ModShelf }, _) => "ModShelf",
        ({ Kind: SchemeHandlerKind.OtherApp }, true) => $"{state.OtherApp ?? "Another app"} opens them now.",
        ({ Kind: SchemeHandlerKind.OtherApp }, false) => state.OtherApp ?? "another app",
        ({ Kind: SchemeHandlerKind.Unavailable }, _) => "unavailable",
        (_, true) => "No app opens them now.",
        _ => "no app",
    };
}
