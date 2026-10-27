using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The accounts linked on each source. Step 5 fills it in.
/// </summary>
public sealed class AccountsViewModel : ScreenViewModel
{
    public override Screen Screen => Screen.Accounts;

    public override string Title => Strings.AccountsTitle;
}
