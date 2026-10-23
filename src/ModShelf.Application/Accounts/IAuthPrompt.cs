using ModShelf.Domain.Mods;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Asks the user for what linking an account needs. The desktop app shows dialogs; the CLI asks on the console.
/// </summary>
/// <remarks>
/// Cancelling the token, or throwing <see cref="OperationCanceledException"/> when the user gives up, ends
/// the linking without changing anything.
/// </remarks>
public interface IAuthPrompt
{
    /// <summary>
    /// For an emailed code: asks which address the source should send it to.
    /// </summary>
    Task<string> AskEmailAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// For an API key: asks the user to paste it.
    /// </summary>
    /// <param name="keyPage">The page where the user finds their key, when the source has one.</param>
    Task<string> AskApiKeyAsync(SourceId source, Uri? keyPage, CancellationToken cancellationToken);

    /// <summary>
    /// For an emailed code: asks for the code.
    /// </summary>
    /// <param name="sentTo">Where the code went, as the source reports it.</param>
    Task<string> AskEmailCodeAsync(SourceId source, string? sentTo, CancellationToken cancellationToken);

    /// <summary>
    /// For OAuth: the authorization page should now be open; linking continues once the user approves there.
    /// </summary>
    /// <param name="authorizeUrl">The page, to show too, so the user can open it by hand.</param>
    /// <param name="browserOpened">Whether the system managed to open the browser.</param>
    void ShowBrowserStep(SourceId source, Uri authorizeUrl, bool browserOpened);
}
