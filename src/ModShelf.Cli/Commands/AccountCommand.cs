using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>account list</c>, <c>account link &lt;source&gt; [--with &lt;method&gt;]</c> and <c>account unlink &lt;source&gt;</c>:
/// the accounts linked on mod sites. Credentials go to the system's secret store, never to the screen or the log.
/// </summary>
internal static class AccountCommand
{
    private const string ApiKeyMethod = "api-key";
    private const string BrowserMethod = "browser";
    private const string EmailMethod = "email";

    public static Command Create(CliEnvironment environment)
    {
        Command list = new("list", "Shows the sources that support accounts, and the account linked on each.");
        list.SetAction((_, cancellationToken) => ListAsync(environment, cancellationToken));

        Argument<string> linkSource = new("source") { Description = "The source's ID, as account list shows it." };
        Option<string?> with = new("--with")
        {
            Description = $"How to sign in: {ApiKeyMethod}, {BrowserMethod} or {EmailMethod}. By default, the first way the source offers.",
            HelpName = "method",
        };
        Command link = new("link", "Links your account on a source, replacing the one linked before.") { linkSource, with };
        link.SetAction((parsed, cancellationToken)
            => LinkAsync(environment, parsed.GetValue(linkSource)!, parsed.GetValue(with), cancellationToken));

        Argument<string> unlinkSource = new("source") { Description = "The source's ID, as account list shows it." };
        Command unlink = new("unlink", "Removes the account linked on a source, with its stored credentials.") { unlinkSource };
        unlink.SetAction((parsed, cancellationToken) => UnlinkAsync(environment, parsed.GetValue(unlinkSource)!, cancellationToken));

        return new Command("account", "Links and unlinks your accounts on mod sites.") { list, link, unlink };
    }

    private static string MethodName(AuthKind kind) => kind switch
    {
        AuthKind.ApiKey => ApiKeyMethod,
        AuthKind.OAuthPkce => BrowserMethod,
        AuthKind.EmailCode => EmailMethod,
        _ => kind.ToString(),
    };

    private static AuthKind? KindNamed(string method) => method switch
    {
        ApiKeyMethod => AuthKind.ApiKey,
        BrowserMethod => AuthKind.OAuthPkce,
        EmailMethod => AuthKind.EmailCode,
        _ => null,
    };

    private static string RefusalMessage(SourceId source, AuthKind kind) => kind switch
    {
        AuthKind.ApiKey => $"{source} refused the API key. Check it, then try again.",
        AuthKind.EmailCode => $"{source} refused the code. Check it, or ask for a new one, then try again.",
        _ => $"{source} didn't let ModShelf in. Try again, and approve ModShelf in the browser.",
    };

    private static async Task<int> ListAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        // Without background downloads, starting the host only creates or migrates the database.
        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<SourceAccountStatus> sources = await host.Services.GetRequiredService<IAccountService>()
                .ListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (sources.Count == 0)
            {
                environment.Output.WriteLine("No source here supports accounts.");
                return ExitCodes.Done;
            }

            string[][] rows =
            [
                ["Source", "Account", "Level", "Linked on", "Link with"],
                .. sources.Select(source => new[]
                {
                    source.Source.Value,
                    source.Account?.Profile.DisplayName ?? (source.WorksWithoutAccount ? "- (optional)" : "-"),
                    source.Account?.Profile.Tier ?? "-",
                    source.Account?.LinkedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-",
                    string.Join(", ", source.AuthKinds.Select(MethodName)),
                }),
            ];
            TextTable.Write(environment.Output, rows);
            WarnWhenNotProtected(environment, host.Services);
            return ExitCodes.Done;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<int> LinkAsync(CliEnvironment environment, string sourceText, string? method, CancellationToken cancellationToken)
    {
        if (!TryReadSource(environment, sourceText, out SourceId? source))
        {
            return ExitCodes.BadInput;
        }

        AuthKind? requested = null;
        if (method is not null && (requested = KindNamed(method)) is null)
        {
            await environment.Error.WriteLineAsync($"Use --with {ApiKeyMethod}, {BrowserMethod} or {EmailMethod}.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        AuthKind kind = default;
        using IHost host = environment.BuildHost(false);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            IAccountService accounts = host.Services.GetRequiredService<IAccountService>();
            SourceAccountStatus? status = (await accounts.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(offered => offered.Source == source);
            if (status is null)
            {
                await environment.Error.WriteLineAsync($"No source called {source} supports accounts. Run account list to see the ones that do.")
                    .ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            kind = requested ?? status.AuthKinds[0];
            if (!status.AuthKinds.Contains(kind))
            {
                await environment.Error.WriteLineAsync(
                        $"{source} can't be linked with --with {MethodName(kind)}. It offers: {string.Join(", ", status.AuthKinds.Select(MethodName))}.")
                    .ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            WarnWhenNotProtected(environment, host.Services);
            LinkedAccount account = await accounts.LinkAsync(source, kind, new TerminalAuthPrompt(environment), cancellationToken)
                .ConfigureAwait(false);
            string tier = account.Profile.Tier is { } level ? $" ({level})" : string.Empty;
            environment.Output.WriteLine($"Linked {source} as {account.Profile.DisplayName}{tier}.");
            return ExitCodes.Done;
        }
        catch (AuthenticationFailedException)
        {
            await environment.Error.WriteLineAsync($"{RefusalMessage(source, kind)} Nothing was linked.").ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            await environment.Error.WriteLineAsync($"Couldn't reach {source}. Check your connection, then try again. Nothing was linked.")
                .ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        catch (ArgumentException exception)
        {
            // The source and the way to link were checked above, so this is about an answer, such as a blank API key.
            await environment.Error.WriteLineAsync($"{exception.MessageForUser()} Nothing was linked.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (Exception exception) when (IsFailureWithReason(exception))
        {
            // Such as a keyring that's locked or doesn't answer, or a site that answered in a way it shouldn't have.
            await environment.Error.WriteLineAsync($"{exception.Message} Nothing was linked.").ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("Stopped. Nothing was linked.").ConfigureAwait(false);
            return ExitCodes.Interrupted;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<int> UnlinkAsync(CliEnvironment environment, string sourceText, CancellationToken cancellationToken)
    {
        if (!TryReadSource(environment, sourceText, out SourceId? source))
        {
            return ExitCodes.BadInput;
        }

        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IAccountService accounts = host.Services.GetRequiredService<IAccountService>();
            SourceAccountStatus? status = (await accounts.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(offered => offered.Source == source);

            // A source that's no longer here can still have an account stored, so unlinking it cleans that up.
            try
            {
                await accounts.UnlinkAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsFailureWithReason(exception))
            {
                await environment.Error.WriteLineAsync($"{exception.Message} The account stays linked; unlink it again once that's fixed.")
                    .ConfigureAwait(false);
                return ExitCodes.Failed;
            }

            environment.Output.WriteLine(status switch
            {
                null => $"{source} isn't a source here. Any account stored for it is removed.",
                { Account: null } => $"No account is linked on {source}.",
                _ => $"Unlinked {source}. Its stored credentials are deleted.",
            });
            return ExitCodes.Done;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether keeping or removing credentials failed: the keyring, Keychain or protected files refused, or a step of
    /// linking went wrong. The message says which, in words the user can act on.
    /// </summary>
    private static bool IsFailureWithReason(Exception exception)
        => exception is InvalidOperationException or IOException or UnauthorizedAccessException or CryptographicException;

    private static bool TryReadSource(CliEnvironment environment, string text, [NotNullWhen(true)] out SourceId? source)
    {
        try
        {
            source = new SourceId(text);
            return true;
        }
        catch (ArgumentException)
        {
            environment.Error.WriteLine($"{text} isn't a source ID. Source IDs look like nexus or mod-io; run account list to see them.");
            source = null;
            return false;
        }
    }

    /// <summary>
    /// The Linux fallback keeps secrets in a file encrypted for this computer; other programs running as the user can
    /// still read it, so the user should know.
    /// </summary>
    private static void WarnWhenNotProtected(CliEnvironment environment, IServiceProvider services)
    {
        if (!services.GetRequiredService<ISecretStore>().IsProtectedByOs)
        {
            environment.Output.WriteLine(
                "No keyring is running, so credentials are stored in a file encrypted for this computer. "
                + "Install and unlock a keyring, such as GNOME Keyring or KWallet, to protect them better.");
        }
    }
}
