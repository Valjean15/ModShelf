using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A way a site offers to link an account, as a button on its card.
/// </summary>
public sealed partial class LinkMethodViewModel : ObservableObject
{
    internal LinkMethodViewModel(AuthKind kind, bool isFirst, IAsyncRelayCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Kind = kind;
        IsFirst = isFirst;
        Command = command;
        Label = kind switch
        {
            AuthKind.ApiKey => Strings.AccountLinkApiKey,
            AuthKind.OAuthPkce => Strings.AccountLinkBrowser,
            AuthKind.EmailCode => Strings.AccountLinkEmail,
            _ => kind.ToString(),
        };
    }

    public AuthKind Kind { get; }

    /// <summary>
    /// Such as "Link with an API key".
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Whether it's the way the site offers first.
    /// </summary>
    public bool IsFirst { get; }

    /// <summary>
    /// Whether the card shows it as its main button: the way the site offers first, while no account is linked.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMain { get; internal set; }

    /// <summary>
    /// Links an account this way.
    /// </summary>
    public IAsyncRelayCommand Command { get; }
}
