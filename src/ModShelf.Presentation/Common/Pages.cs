using ModShelf.Application.Accounts.Ports;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Opens web pages for the user.
/// </summary>
internal static class Pages
{
    /// <summary>
    /// Opens <paramref name="page"/> in the browser; when none opens, shows its address in a dialog, to open by hand.
    /// </summary>
    /// <param name="openByHand">What the dialog says, with the address, such as "Open this page in your browser: {0}".</param>
    public static async Task OpenAsync(Uri page, IBrowserLauncher browser, IDialogService dialogs, string openByHand, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(dialogs);

        if (!await browser.TryOpenAsync(page, cancellationToken))
        {
            await dialogs.ShowMessageAsync(Strings.BrowserDidntOpenTitle, Text.Format(openByHand, page.AbsoluteUri), cancellationToken);
        }
    }
}
