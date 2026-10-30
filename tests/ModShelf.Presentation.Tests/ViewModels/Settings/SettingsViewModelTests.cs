using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

public sealed class SettingsViewModelTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly FolderPattern ById = FolderPattern.Parse("{source}/{game}/{modid}/{file}");

    private static readonly FolderPattern Flat = FolderPattern.Parse("{mod}/{file}");

    private readonly SettingsHarness _harness = new();

    private static string Moved => Path.Combine(Path.GetTempPath(), "Moved Library");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private SettingsViewModel Screen => _harness.Screen;

    [Fact]
    public async Task ShowsTheDefaultsWithoutSavingThem()
    {
        await _harness.StartAsync();

        Assert.True(Screen.IsLoaded);
        Assert.Null(Screen.Problem);
        Assert.Equal($"Settings are kept in {_harness.Settings.Location}.", Screen.KeptInText);
        Assert.Equal((TestData.LibraryRoot, true), (Screen.LibraryRoot, Screen.IsDefaultRoot));
        Assert.Equal(FolderPattern.Default.Text, Screen.Folders.Text);
        Assert.Equal(
            "For example: " + Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "SkyUI (12604)", "SkyUI_5_2_SE.7z"),
            Screen.Folders.Preview);
        Assert.Equal(("Every game", false, false, false), (Screen.Folders.Name, Screen.Folders.IsGame, Screen.Folders.IsChanged, Screen.Folders.IsNotDefault));
        Assert.Empty(Screen.GameFolders);
        Assert.Equal((false, false, true, 2), (Screen.IsKeepAll, Screen.IsArchive, Screen.IsKeepLatest, Screen.KeepCount));
        Assert.Equal(
            [
                "Every 15 minutes", "Every 30 minutes", "Every hour", "Every 2 hours", "Every 3 hours", "Every 6 hours (the default)",
                "Every 12 hours", "Every day", "Every 2 days", "Every 7 days",
            ],
            Screen.IntervalChoices.Select(choice => choice.Label));
        Assert.Equal(TimeSpan.FromHours(6), Screen.SelectedInterval?.Interval);
        Assert.True(Screen.NotifyOfUpdates);
        Assert.Equal((AppTheme.System, true, false, false), (Screen.Theme, Screen.IsSystemTheme, Screen.IsLightTheme, Screen.IsDarkTheme));
        Assert.True(Screen.ChooseRootCommand.CanExecute(null));
        Assert.False(Screen.UseDefaultRootCommand.CanExecute(null));
        Assert.True(Screen.AddGameCommand.CanExecute(null));

        await Screen.Saving.WaitAsync(Patience, Cancellation);
        Assert.Empty(_harness.Settings.Changes);
        Assert.Empty(_harness.Themes.Applied);
    }

    [Fact]
    public async Task ShowsTheStoredSettingsAndAppliesTheTheme()
    {
        _harness.Settings.Stored = new AppSettings
        {
            LibraryRoot = Moved,
            LibraryFolders = ById,
            GameFolders = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [new KeyValuePair<string, FolderPattern>("stardewvalley", Flat), new KeyValuePair<string, FolderPattern>("Fallout4", ById)]),
            OldVersions = OldVersionRule.Archive,
            UpdateInterval = TimeSpan.FromMinutes(90),
            NotifyOfUpdates = false,
            Theme = AppTheme.Dark,
        };

        await _harness.StartAsync();

        Assert.Equal((Moved, false), (Screen.LibraryRoot, Screen.IsDefaultRoot));
        Assert.True(Screen.UseDefaultRootCommand.CanExecute(null));
        Assert.Equal((ById.Text, true), (Screen.Folders.Text, Screen.Folders.IsNotDefault));
        Assert.Equal(["Fallout4", "stardewvalley"], Screen.GameFolders.Select(row => row.Game));
        FolderPatternViewModel stardew = Screen.GameFolders[1];
        Assert.Equal((Flat.Text, true, "stardewvalley"), (stardew.Text, stardew.IsGame, stardew.Name));

        // A game's pattern is removed rather than set back.
        Assert.False(stardew.IsNotDefault);
        Assert.False(stardew.UseDefaultCommand.CanExecute(null));
        Assert.Equal("For example: " + Path.Combine(Moved, "SkyUI", "SkyUI_5_2_SE.7z"), stardew.Preview);
        Assert.Equal(
            "For example: " + Path.Combine(Moved, "nexus", "Fallout4", "12604", "SkyUI_5_2_SE.7z"),
            Screen.GameFolders[0].Preview);
        Assert.Equal((false, true, false), (Screen.IsKeepAll, Screen.IsArchive, Screen.IsKeepLatest));
        Assert.Equal("Every 90 minutes", Screen.SelectedInterval?.Label);
        Assert.Equal("Every 90 minutes", Screen.IntervalChoices[3].Label);
        Assert.False(Screen.NotifyOfUpdates);
        Assert.True(Screen.IsDarkTheme);
        Assert.Equal([AppTheme.Dark], _harness.Themes.Applied);

        await Screen.Saving.WaitAsync(Patience, Cancellation);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task ChoicesAreSavedAsTheyreMadeAndDefaultsLeaveTheSettings()
    {
        await _harness.StartAsync();

        Screen.IsArchive = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);
        OldVersionRule? archived = _harness.Settings.Stored.OldVersions;
        Screen.IsKeepLatest = true;
        Screen.KeepCount = 5;
        await Screen.Saving.WaitAsync(Patience, Cancellation);
        OldVersionRule? keepFive = _harness.Settings.Stored.OldVersions;
        Screen.KeepCount = 2;
        Screen.SelectedInterval = Screen.IntervalChoices.Single(choice => choice.Interval == TimeSpan.FromDays(1));
        Screen.NotifyOfUpdates = false;
        Screen.IsKeepAll = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal(OldVersionRule.Archive, archived);
        Assert.Equal(OldVersionRule.KeepLatest(5), keepFive);
        Assert.Equal(
            ["oldVersions archive", "oldVersions null", "oldVersions 5", "oldVersions null", "interval 1.00:00:00", "notify False", "oldVersions all"],
            _harness.Settings.Changes);
        Assert.Equal(OldVersionRule.KeepAll, _harness.Settings.Stored.OldVersions);
        Assert.Equal(TimeSpan.FromDays(1), _harness.Settings.Stored.UpdateInterval);
        Assert.False(_harness.Settings.Stored.NotifyOfUpdates);
        Assert.Null(Screen.Problem);
    }

    [Fact]
    public async Task ChoicesBackAtTheirDefaultsLeaveTheSettings()
    {
        _harness.Settings.Stored = new AppSettings { UpdateInterval = TimeSpan.FromDays(1), NotifyOfUpdates = false, Theme = AppTheme.Light };
        await _harness.StartAsync();

        Screen.SelectedInterval = Screen.IntervalChoices.Single(choice => choice.Interval == TimeSpan.FromHours(6));
        Screen.NotifyOfUpdates = true;
        Screen.IsSystemTheme = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal(["interval null", "notify null", "theme null"], _harness.Settings.Changes);
        Assert.Equal((null, null, null), (_harness.Settings.Stored.UpdateInterval, _harness.Settings.Stored.NotifyOfUpdates, _harness.Settings.Stored.Theme));
    }

    [Fact]
    public async Task AThemeIsAppliedAtOnceAndSaved()
    {
        await _harness.StartAsync();

        Screen.IsDarkTheme = true;
        AppTheme[] appliedBeforeSaving = [.. _harness.Themes.Applied];
        await Screen.Saving.WaitAsync(Patience, Cancellation);
        Screen.IsLightTheme = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal([AppTheme.Dark], appliedBeforeSaving);
        Assert.Equal([AppTheme.Dark, AppTheme.Light], _harness.Themes.Applied);
        Assert.Equal(["theme Dark", "theme Light"], _harness.Settings.Changes);
        Assert.Equal(AppTheme.Light, _harness.Settings.Stored.Theme);
    }

    [Fact]
    public async Task UncheckingARadioButtonChoosesNothing()
    {
        await _harness.StartAsync();

        Screen.IsArchive = false;
        Screen.IsKeepAll = false;
        Screen.IsDarkTheme = false;
        Screen.IsLightTheme = false;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.True(Screen.IsKeepLatest);
        Assert.True(Screen.IsSystemTheme);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task HowManyVersionsToKeepIsSavedOnlyWhileTheyreKept()
    {
        _harness.Settings.Stored = new AppSettings { OldVersions = OldVersionRule.Archive };
        await _harness.StartAsync();

        Screen.KeepCount = 4;
        await Screen.Saving.WaitAsync(Patience, Cancellation);
        Screen.IsKeepLatest = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal(["oldVersions 4"], _harness.Settings.Changes);
    }

    [Fact]
    public async Task ChoicesAreSavedOneAtATimeInTheOrderMade()
    {
        await _harness.StartAsync();
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Settings.Hold = hold;

        Screen.IsDarkTheme = true;
        Screen.NotifyOfUpdates = false;
        IReadOnlyList<string> whileHeld = _harness.Settings.Changes;
        _harness.Settings.Hold = null;
        hold.SetResult();
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal(["theme Dark"], whileHeld);
        Assert.Equal(["theme Dark", "notify False"], _harness.Settings.Changes);
        Assert.Equal((AppTheme.Dark, false), (_harness.Settings.Stored.Theme, _harness.Settings.Stored.NotifyOfUpdates));
        Assert.True(Screen.IsDarkTheme);
        Assert.False(Screen.NotifyOfUpdates);
    }

    [Fact]
    public async Task AChoiceThatCantBeSavedSaysWhyAndShowsWhatsStored()
    {
        await _harness.StartAsync();
        _harness.Settings.SaveRefusal = new IOException("The settings couldn't be saved to settings.json: No space left on device");

        Screen.IsDarkTheme = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal("The settings couldn't be saved to settings.json: No space left on device", Screen.Problem);
        Assert.True(Screen.IsSystemTheme);
        Assert.Equal([AppTheme.Dark, AppTheme.System], _harness.Themes.Applied);

        _harness.Settings.SaveRefusal = null;
        Screen.IsLightTheme = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Null(Screen.Problem);
    }

    [Fact]
    public async Task AChoiceThatFailsUnexpectedlyIsLogged()
    {
        await _harness.StartAsync();
        _harness.Settings.SaveRefusal = new NotSupportedException("Boom.");

        Screen.NotifyOfUpdates = false;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal(Strings.SomethingWentWrong, Screen.Problem);
        Assert.True(Screen.NotifyOfUpdates);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Changing the settings failed System.NotSupportedException: Boom.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsThatCantBeReadSaySoAndCantBeChanged()
    {
        _harness.Settings.ReadRefusal = new InvalidDataException("The settings in settings.json can't be read: '}' is invalid. Fix the file, or delete it to start again.");

        await _harness.StartAsync();

        Assert.False(Screen.IsLoaded);
        Assert.Equal("The settings in settings.json can't be read: '}' is invalid. Fix the file, or delete it to start again.", Screen.Problem);
        Assert.False(Screen.ChooseRootCommand.CanExecute(null));
        Assert.False(Screen.UseDefaultRootCommand.CanExecute(null));
        Assert.False(Screen.AddGameCommand.CanExecute(null));
        Assert.Empty(_harness.Themes.Applied);
    }

    [Fact]
    public async Task SettingsThatCantBeReadAnyMoreAfterAChangeSaySo()
    {
        await _harness.StartAsync();
        _harness.Settings.ReadRefusal = new InvalidDataException("The settings in settings.json can't be read.");

        Screen.NotifyOfUpdates = false;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal("The settings in settings.json can't be read.", Screen.Problem);
        Assert.False(Screen.IsLoaded);
    }

    [Fact]
    public async Task WhatClosingTheWindowDoesIsSavedAsItsChosen()
    {
        await _harness.StartAsync();
        bool askedAtFirst = Screen.IsAskOnClose;

        Screen.IsCloseToTray = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);
        bool? kept = _harness.Settings.Stored.CloseToTray;
        Screen.IsQuitOnClose = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);
        bool? quits = _harness.Settings.Stored.CloseToTray;
        Screen.IsAskOnClose = true;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.True(askedAtFirst);
        Assert.Equal((true, false), (kept, quits));
        Assert.Null(_harness.Settings.Stored.CloseToTray);
        Assert.Equal(["closeToTray True", "closeToTray False", "closeToTray null"], _harness.Settings.Changes);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task ShowsWhatClosingTheWindowDoes(bool closeToTray, bool ask, bool tray, bool quit)
    {
        _harness.Settings.Stored = new AppSettings { CloseToTray = closeToTray };

        await _harness.StartAsync();
        Screen.IsAskOnClose = false;
        Screen.IsCloseToTray = false;
        Screen.IsQuitOnClose = false;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal((ask, tray, quit), (Screen.IsAskOnClose, Screen.IsCloseToTray, Screen.IsQuitOnClose));
        Assert.Empty(_harness.Settings.Changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaysWhetherTheSystemShowsTrayIcons(bool available)
    {
        _harness.Tray.IsAvailable = available;
        bool before = Screen.IsTrayAvailable;

        await _harness.StartAsync();

        Assert.True(before);
        Assert.Equal(available, Screen.IsTrayAvailable);
        Assert.Equal(1, _harness.Tray.Asked);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WarnsWhenNoKeyringProtectsTheCredentials(bool protectedByOs, bool warns)
    {
        SettingsHarness harness = new(protectedByOs);

        Assert.Equal(warns, harness.Screen.IsStoreUnprotected);
    }

    [Fact]
    public async Task APatternIsCheckedAsItsTypedAndSavedWhenAsked()
    {
        await _harness.StartAsync();
        FolderPatternViewModel folders = Screen.Folders;

        folders.Text = "{mod}";
        (string? problem, string? preview, bool canSave) wrong = (folders.Problem, folders.Preview, folders.SaveCommand.CanExecute(null));
        folders.Text = ById.Text;
        (string? problem, string? preview, bool changed, bool canSave) right = (folders.Problem, folders.Preview, folders.IsChanged, folders.SaveCommand.CanExecute(null));
        await folders.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("'{mod}' isn't a folder pattern. The last part must be exactly {file}.", null, false), wrong);
        Assert.Equal(
            (null, "For example: " + Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "12604", "SkyUI_5_2_SE.7z"), true, true),
            right);
        Assert.Equal(["folders * {source}/{game}/{modid}/{file}"], _harness.Settings.Changes);
        Assert.Equal(ById, _harness.Settings.Stored.LibraryFolders);
        Assert.False(folders.IsChanged);
        Assert.False(folders.SaveCommand.CanExecute(null));
        Assert.True(folders.UseDefaultCommand.CanExecute(null));

        await folders.UseDefaultCommand.ExecuteAsync(null);

        Assert.Null(_harness.Settings.Stored.LibraryFolders);
        Assert.Equal(FolderPattern.Default.Text, folders.Text);
        Assert.False(folders.UseDefaultCommand.CanExecute(null));
    }

    [Fact]
    public async Task ThePatternTypedBackToTheStoredOneIsntAChange()
    {
        await _harness.StartAsync();

        Screen.Folders.Text = Flat.Text;
        Screen.Folders.Text = FolderPattern.Default.Text;

        Assert.False(Screen.Folders.IsChanged);
        Assert.False(Screen.Folders.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task APatternThatCantBeSavedSaysWhyAndKeepsTheEdit()
    {
        await _harness.StartAsync();
        _harness.Settings.SaveRefusal = new IOException("The settings couldn't be saved to settings.json: Access denied");

        Screen.Folders.Text = Flat.Text;
        await Screen.Folders.SaveCommand.ExecuteAsync(null);

        Assert.Equal("The settings couldn't be saved to settings.json: Access denied", Screen.Folders.Problem);
        Assert.Equal((Flat.Text, true), (Screen.Folders.Text, Screen.Folders.IsChanged));
        Assert.Null(Screen.Problem);
    }

    [Fact]
    public async Task AnEditInProgressSurvivesOtherChanges()
    {
        await _harness.StartAsync();

        Screen.Folders.Text = Flat.Text;
        Screen.NotifyOfUpdates = false;
        await Screen.Saving.WaitAsync(Patience, Cancellation);

        Assert.Equal((Flat.Text, true), (Screen.Folders.Text, Screen.Folders.IsChanged));
        Assert.Null(_harness.Settings.Stored.LibraryFolders);
    }

    [Fact]
    public async Task AGamesOwnPatternIsAddedEditedAndRemoved()
    {
        _harness.Settings.Stored = new AppSettings { LibraryFolders = ById };
        await _harness.StartAsync();
        _harness.Dialogs.Answers.Enqueue(" stardewvalley ");

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Equal(
            new TextQuestion(
                "Add a game's folders",
                "Enter the game as its site names it in links, such as skyrimspecialedition for Skyrim Special Edition on Nexus Mods. Its files then go by a pattern of their own.",
                "skyrimspecialedition",
                "Add"),
            Assert.Single(_harness.Dialogs.Questions));
        FolderPatternViewModel stardew = Assert.Single(Screen.GameFolders);
        Assert.Equal(("stardewvalley", ById.Text), (stardew.Game, stardew.Text));
        Assert.Equal(ById, _harness.Settings.Stored.FoldersFor("stardewvalley"));
        Assert.Null(Screen.GameFoldersNote);

        stardew.Text = Flat.Text;
        await stardew.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Flat, _harness.Settings.Stored.FoldersFor("StardewValley"));
        Assert.Same(stardew, Assert.Single(Screen.GameFolders));
        Assert.False(stardew.IsChanged);

        await stardew.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(Screen.GameFolders);
        Assert.Empty(_harness.Settings.Stored.GameFolders);
        Assert.Equal(
            ["folders stardewvalley {source}/{game}/{modid}/{file}", "folders stardewvalley {mod}/{file}", "folders stardewvalley null"],
            _harness.Settings.Changes);
    }

    [Fact]
    public async Task AGameWithAPatternAlreadyIsntAddedAgain()
    {
        _harness.Settings.Stored = new AppSettings
        {
            GameFolders = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, [new KeyValuePair<string, FolderPattern>("stardewvalley", Flat)]),
        };
        await _harness.StartAsync();
        _harness.Dialogs.Answers.Enqueue("StardewValley");

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Equal("StardewValley has a pattern of its own already.", Screen.GameFoldersNote);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task NoGameEnteredAddsNothing(string? answer)
    {
        await _harness.StartAsync();
        _harness.Dialogs.Answers.Enqueue(answer);

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Empty(Screen.GameFolders);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task AnEmptyLibraryChangesFolderWithoutAsking()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(Moved);

        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.Equal("Choose the library's folder", Assert.Single(_harness.Dialogs.FolderPickers));
        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Empty(_harness.Mover.Moves);
        Assert.Equal([$"root {Moved}"], _harness.Settings.Changes);
        Assert.Equal((Moved, false), (Screen.LibraryRoot, Screen.IsDefaultRoot));
        Assert.Equal(($"The library is in {Moved} now.", false), (Screen.RootNote, Screen.IsRootNoteAProblem));
        Assert.Equal("For example: " + Path.Combine(Moved, "nexus", "skyrimspecialedition", "SkyUI (12604)", "SkyUI_5_2_SE.7z"), Screen.Folders.Preview);

        await Screen.UseDefaultRootCommand.ExecuteAsync(null);

        Assert.Equal("root null", _harness.Settings.Changes[^1]);
        Assert.Equal((TestData.LibraryRoot, true), (Screen.LibraryRoot, Screen.IsDefaultRoot));
    }

    [Fact]
    public async Task ALibraryWithFilesMovesThemOnceTheUserConfirms()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1"), "SkyUI", true, TestData.LibraryFile(TestData.Mod("1"), "10", "a.7z"), TestData.LibraryFile(TestData.Mod("1"), "11", "b.7z")));
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(Moved);
        _harness.Dialogs.Folders.Enqueue(Moved);
        _harness.Dialogs.Confirm.Enqueue(false);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Mover.Missing = 1;

        await Screen.ChooseRootCommand.ExecuteAsync(null);
        string rootAfterDeclining = Screen.LibraryRoot;
        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.Equal(TestData.LibraryRoot, rootAfterDeclining);
        Assert.Equal(
            ("Move the library?", $"ModShelf moves the library's 2 files from {TestData.LibraryRoot} to {Moved}, into the same folders there. Other files in {TestData.LibraryRoot} stay where they are.", "Move"),
            _harness.Dialogs.Confirmations[0]);
        Assert.Equal([Moved], _harness.Mover.Moves);
        Assert.Empty(_harness.Settings.Changes);
        Assert.Equal(Moved, Screen.LibraryRoot);
        Assert.Equal(
            $"The library is in {Moved} now. One of its files wasn't on disk; it stays in the library, to download again.",
            Screen.RootNote);
        Assert.False(Screen.IsMoving);
    }

    [Fact]
    public async Task MovingOneFileSaysSo()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1"), "SkyUI", true, TestData.LibraryFile(TestData.Mod("1"), "10", "a.7z")));
        _harness.Settings.Stored = new AppSettings { LibraryRoot = Moved };
        await _harness.StartAsync();
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Mover.Missing = 3;

        await Screen.UseDefaultRootCommand.ExecuteAsync(null);

        Assert.Equal(
            $"ModShelf moves the library's file from {Moved} to {TestData.LibraryRoot}, into the same folders there. Other files in {Moved} stay where they are.",
            Assert.Single(_harness.Dialogs.Confirmations).Message);
        Assert.Equal([TestData.LibraryRoot], _harness.Mover.Moves);
        Assert.True(Screen.IsDefaultRoot);
        Assert.Equal(
            $"The library is in {TestData.LibraryRoot} now. 3 of its files weren't on disk; they stay in the library, to download again.",
            Screen.RootNote);
    }

    [Fact]
    public async Task AMoveThatFailsSaysWhyAndKeepsTheRoot()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1"), "SkyUI", true, TestData.LibraryFile(TestData.Mod("1"), "10", "a.7z")));
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(Moved);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Mover.Refusal = new IOException("a.7z couldn't be moved: it's in use. Every file went back.");

        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.Equal(("a.7z couldn't be moved: it's in use. Every file went back.", true), (Screen.RootNote, Screen.IsRootNoteAProblem));
        Assert.Equal(TestData.LibraryRoot, Screen.LibraryRoot);
        Assert.False(Screen.IsMoving);
        Assert.True(Screen.ChooseRootCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARootThatCantChangeYetSaysWhy()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(Moved);
        _harness.Settings.RootRefusal = new InvalidOperationException($"A download into {TestData.LibraryRoot} isn't finished. Let it finish, or cancel it, first.");

        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.Equal(($"A download into {TestData.LibraryRoot} isn't finished. Let it finish, or cancel it, first.", true), (Screen.RootNote, Screen.IsRootNoteAProblem));
        Assert.Equal(TestData.LibraryRoot, Screen.LibraryRoot);
    }

    [Fact]
    public async Task ARootChangeThatFailsUnexpectedlyIsLogged()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(Moved);
        _harness.Settings.RootRefusal = new NotSupportedException("Boom.");

        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.Equal((Strings.SomethingWentWrong, true), (Screen.RootNote, Screen.IsRootNoteAProblem));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Changing the library's folder failed System.NotSupportedException: Boom.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PickingNoFolderOrTheSameOneChangesNothing()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1"), "SkyUI", true, TestData.LibraryFile(TestData.Mod("1"), "10", "a.7z")));
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(null);
        _harness.Dialogs.Folders.Enqueue(TestData.LibraryRoot + Path.DirectorySeparatorChar);

        await Screen.ChooseRootCommand.ExecuteAsync(null);
        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Empty(_harness.Mover.Moves);
        Assert.Empty(_harness.Settings.Changes);
        Assert.Null(Screen.RootNote);
    }

    [Fact]
    public async Task WhileTheLibraryMovesItsFolderCantChange()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1"), "SkyUI", true, TestData.LibraryFile(TestData.Mod("1"), "10", "a.7z")));
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(Moved);
        _harness.Dialogs.Confirm.Enqueue(true);
        bool? couldChooseWhileMoving = null;
        _harness.Mover.Refusal = null;
        Screen.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsMoving) && Screen.IsMoving)
            {
                couldChooseWhileMoving = Screen.ChooseRootCommand.CanExecute(null) || Screen.UseDefaultRootCommand.CanExecute(null);
            }
        };

        await Screen.ChooseRootCommand.ExecuteAsync(null);

        Assert.False(couldChooseWhileMoving);
        Assert.True(Screen.UseDefaultRootCommand.CanExecute(null));
    }
}
