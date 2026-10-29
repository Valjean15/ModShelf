namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// One of a <see cref="ChoiceDialog"/>'s buttons.
/// </summary>
/// <param name="Index">Its place among the choices; the first is the main one.</param>
internal sealed record ChoiceDialogButton(string Label, int Index)
{
    public bool IsMain => Index == 0;
}
