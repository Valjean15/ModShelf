using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A folder pattern on the Settings screen, for every game or for one, edited as text and saved when the user says. It
/// shows where a sample file would go, or what's wrong with the pattern.
/// </summary>
public sealed partial class FolderPatternViewModel : ObservableObject
{
    // A sample mod and file, for the preview: real enough to show what each part becomes.
    private const string SampleSource = "nexus";
    private const string SampleGame = "skyrimspecialedition";
    private const string SampleMod = "SkyUI";
    private const string SampleModId = "12604";
    private const string SampleFile = "SkyUI_5_2_SE.7z";

    private readonly Func<GameRef?, FolderPattern?, Task<string?>> _save;
    private FolderPattern _saved;
    private string _root;

    /// <param name="game">The game; null for every game.</param>
    /// <param name="saved">The pattern as it's stored.</param>
    /// <param name="root">The library's root, for the preview.</param>
    /// <param name="save">
    /// Saves a game's pattern, or every game's with a null game; a null pattern sets it back. Returns what went wrong, or
    /// null.
    /// </param>
    internal FolderPatternViewModel(GameRef? game, FolderPattern saved, string root, Func<GameRef?, FolderPattern?, Task<string?>> save)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(save);

        Game = game;
        _saved = saved;
        _root = root;
        _save = save;
        Text = saved.Text;
    }

    /// <summary>
    /// The game; null for every game.
    /// </summary>
    public GameRef? Game { get; }

    /// <summary>
    /// The game as its site names it in links, or "Every game".
    /// </summary>
    public string Name => Game?.Key ?? Strings.SettingsFoldersEveryGame;

    /// <summary>
    /// Whether it's a game's own pattern, which can be removed, rather than every game's, which can be set back.
    /// </summary>
    public bool IsGame => Game is not null;

    /// <summary>
    /// The pattern as the user types it.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Such as "For example: /home/jean/Downloads/ModShelf/nexus/…/SkyUI_5_2_SE.7z"; null while the pattern is wrong.
    /// </summary>
    [ObservableProperty]
    public partial string? Preview { get; private set; }

    /// <summary>
    /// What's wrong with the pattern as typed, or why it couldn't be saved; null for nothing.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// Whether the text differs from the pattern stored.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsChanged { get; private set; }

    /// <summary>
    /// Whether every game's pattern is something other than the default, which <see cref="UseDefaultCommand"/> sets back.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseDefaultCommand))]
    public partial bool IsNotDefault { get; private set; }

    /// <summary>
    /// Shows the pattern as it's stored now, and the library's root, unless the user has changed the text. Runs on the
    /// UI thread.
    /// </summary>
    internal void Show(FolderPattern saved, string root)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(root);

        bool keepEdit = IsChanged;
        _saved = saved;
        _root = root;
        if (keepEdit)
        {
            Check();
        }
        else
        {
            // Setting the same text raises no change, so the check runs here too.
            Text = saved.Text;
            Check();
        }
    }

    private bool CanSave() => IsChanged && Problem is null;

    partial void OnTextChanged(string value) => Check();

    /// <summary>
    /// Reads the text as a pattern: shows where the sample file would go, or what's wrong.
    /// </summary>
    private void Check()
    {
        IsChanged = !string.Equals(Text, _saved.Text, StringComparison.Ordinal);
        IsNotDefault = !IsGame && _saved != FolderPattern.Default;
        try
        {
            FolderPattern pattern = FolderPattern.Parse(Text);
            Preview = Common.Text.Format(
                Strings.SettingsFoldersPreview,
                LibraryPaths.Resolve(_root, pattern.Render(Game?.Source.Value ?? SampleSource, Game?.Key ?? SampleGame, SampleMod, SampleModId, SampleFile)));
            Problem = null;
        }
        catch (ArgumentException exception)
        {
            Preview = null;
            Problem = exception.MessageForUser();
        }
    }

    /// <summary>
    /// Saves the pattern as typed.
    /// </summary>
    /// <summary>
    /// Saves <paramref name="pattern"/> as the pattern, such as every game's as a game's own.
    /// </summary>
    /// <returns>What went wrong; null when nothing did.</returns>
    internal Task<string?> SaveAsAsync(FolderPattern pattern) => _save(Game, pattern);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        FolderPattern pattern = FolderPattern.Parse(Text);
        if (await _save(Game, pattern) is { } problem)
        {
            Problem = problem;
        }
    }

    /// <summary>
    /// Sets every game's pattern back to the default.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsNotDefault))]
    private async Task UseDefaultAsync()
    {
        if (await _save(null, null) is { } problem)
        {
            Problem = problem;
        }
    }

    /// <summary>
    /// Removes the game's own pattern, so its files go by every game's again.
    /// </summary>
    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (Game is not null && await _save(Game, null) is { } problem)
        {
            Problem = problem;
        }
    }
}
