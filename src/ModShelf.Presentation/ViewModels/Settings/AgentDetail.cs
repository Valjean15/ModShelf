using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One line of the MCP server's details, with what it holds to copy, such as the command line's path.
/// </summary>
public sealed partial class AgentDetail : ObservableObject
{
    private readonly IClipboard _clipboard;

    /// <param name="isCode">Whether the value is code, such as JSON or a command, which shows in a monospaced font.</param>
    public AgentDetail(string label, string value, bool isCode, IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);

        Label = label;
        Value = value;
        IsCode = isCode;
        _clipboard = clipboard;
    }

    public string Label { get; }

    public string Value { get; }

    public bool IsCode { get; }

    /// <summary>
    /// Whether <see cref="Value"/> was just copied, which the button says.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        await _clipboard.SetTextAsync(Value, cancellationToken);
        IsCopied = true;
    }
}
