using System.CommandLine;
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Endorsements;
using ModShelf.Application.Sources;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod endorse &lt;link&gt;</c> and <c>mod abstain &lt;link&gt;</c>: the linked account's endorsement of a mod on its
/// site, such as Nexus Mods, said in the site's words (ADR 0100).
/// </summary>
internal static partial class ModCommand
{
    private static Command CreateEndorse(CliEnvironment environment, bool endorse)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };

        Command command = endorse
            ? new("endorse", "Endorses the mod on its site with the linked account, as the site's button does, such as Endorse on Nexus Mods.") { link }
            : new("abstain", "Chooses not to endorse the mod on its site, or takes the endorsement back, such as Abstain on Nexus Mods.") { link };
        command.SetAction((parsed, cancellationToken) => EndorseAsync(environment, parsed.GetValue(link)!, endorse, cancellationToken));
        return command;
    }

    private static async Task<int> EndorseAsync(CliEnvironment environment, string linkText, bool endorse, CancellationToken cancellationToken)
    {
        if (!ModLinkArgument.TryRead(environment, linkText, out Uri? link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    ModRef mod;
                    try
                    {
                        mod = await services.GetRequiredService<IEndorsements>()
                            .EndorseAsync(link, endorse ? Endorsement.Endorsed : Endorsement.Abstained, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (EndorsingProblem(exception, endorse ? "endorse the mod" : "abstain from the mod") is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    // In the site's words: "Endorsed on Nexus Mods: mod 2400 of stardewvalley.", or "Abstained on …".
                    ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
                    string word = (endorse ? CliText.Words(sources, mod.Game.Source).Approve : CliText.Words(sources, mod.Game.Source).Unapprove)
                        ?? (endorse ? "endorse" : "abstain");
                    string done = word.EndsWith('e') ? $"{word}d" : $"{word}ed";
                    await environment.Output.WriteLineAsync(
                            CliText.Capitalized($"{done} on {CliText.SiteName(sources, mod.Game.Source)}: mod {mod.Id} of {mod.Game.Key}."))
                        .ConfigureAwait(false);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What to say when a mod can't be endorsed, or abstained from: the site's refusal in its own words, such as that the
    /// account didn't download the mod.
    /// </summary>
    /// <param name="doing">What failed, such as "endorse the mod".</param>
    private static (string Message, int ExitCode)? EndorsingProblem(Exception exception, string doing) => exception switch
    {
        AuthenticationFailedException => ($"The site needs a linked account to {doing}, or refused the one linked. {LinkAccountAgain}", ExitCodes.Failed),
        HttpRequestException { StatusCode: not (HttpStatusCode.NotFound or HttpStatusCode.Gone) } failed
            => ($"The site wouldn't {doing}: {failed.Message}", ExitCodes.Failed),
        _ => LinkProblems.Describe(exception),
    };
}
