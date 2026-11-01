using System.ComponentModel;

using ModelContextProtocol.Server;

using ModShelf.Application.Accounts;
using ModShelf.Application.Diagnostics;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools that say how ModShelf is: the sites' accounts, and the report a bug report needs. Neither holds keys,
/// account names or links.
/// </summary>
[McpServerToolType]
internal sealed class StatusTools(IAccountService accounts, IDiagnostics diagnostics)
{
    [McpServerTool(Name = "accounts_status", Title = "The sites' accounts", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Says, for each site that has accounts, whether ModShelf has one linked and at what level, such as Premium on Nexus Mods, whose downloads start without the user. Never whose account it is, or its key. Linking is for the user, in the desktop app or with the CLI's account link.")]
    public Task<AccountsListing> AccountsAsync(CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            IReadOnlyList<SourceAccountStatus> sources = await accounts.ListAsync(cancellationToken).ConfigureAwait(false);
            return new AccountsListing(
            [
                .. sources.Select(source => new AccountEntry(
                    source.Source.Value,
                    source.DisplayName,
                    source.Account is not null,
                    source.Account?.Profile.Tier,
                    source.Account?.LinkedAt,
                    source.WorksWithoutAccount,
                    [.. source.AuthKinds.Select(Method)])),
            ]);
        });

    [McpServerTool(Name = "diagnostics", Title = "Diagnostics report", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Writes the report a bug report needs: ModShelf's version and system, its folders, the settings, the library, the queue and the linked accounts. It holds no keys, account names or links.")]
    public Task<DiagnosticsReport> DiagnosticsAsync(CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () => new DiagnosticsReport(await diagnostics.DescribeAsync(cancellationToken).ConfigureAwait(false)));

    /// <summary>
    /// A way to link an account, as the CLI's account link names it.
    /// </summary>
    private static string Method(AuthKind kind) => kind switch
    {
        AuthKind.ApiKey => "api-key",
        AuthKind.OAuthPkce => "browser",
        AuthKind.EmailCode => "email",
        _ => kind.ToString(),
    };
}
