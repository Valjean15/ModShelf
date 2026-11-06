using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Agents;
using ModShelf.Application.Agents.Ports;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Settings › Agents: whether AI agents may use ModShelf through its MCP server, off until the user turns it on; the
/// address the app serves meanwhile, and its port; and connecting an agent: a prompt to paste into its chat, the details
/// to configure its app by hand, and adding the server to Claude Desktop (ADR 0080).
/// </summary>
public sealed partial class AgentsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IAgentServer _server;
    private readonly IClaudeDesktopConfig _claudeDesktop;
    private readonly IClipboard _clipboard;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IUiThread _uiThread;
    private readonly ILogger<AgentsViewModel> _logger;

    // While the settings are shown as read, a change of the toggle isn't the user's.
    private bool _showing;
    private int _port = AppSettings.DefaultAgentsPort;

    public AgentsViewModel(
        ISettingsService settings,
        IAgentServer server,
        IClaudeDesktopConfig claudeDesktop,
        IClipboard clipboard,
        IDialogService dialogs,
        INavigationService navigation,
        IUiThread uiThread,
        ILogger<AgentsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(claudeDesktop);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _server = server;
        _claudeDesktop = claudeDesktop;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _navigation = navigation;
        _uiThread = uiThread;
        _logger = logger;
        PortText = _port.ToString(CultureInfo.CurrentCulture);
        Status = Describe(server.State);
        server.StateChanged += OnStateChanged;
        navigation.Navigated += OnNavigated;
    }

    /// <summary>
    /// Whether agents may use ModShelf: changing it saves it, and serves the address or stops serving it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>
    /// The address's port, as typed; <see cref="SavePortCommand"/> saves it.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePortCommand))]
    public partial string PortText { get; set; }

    /// <summary>
    /// Where the address stands, such as "Agents can also reach ModShelf at http://127.0.0.1:47823/mcp while it runs."
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; }

    /// <summary>
    /// Whether the address can't be served, which the status says in the warm colour.
    /// </summary>
    [ObservableProperty]
    public partial bool HasStatusProblem { get; private set; }

    /// <summary>
    /// What went wrong saving or applying a change; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// What was just done, such as "The prompt is copied: paste it into your agent's chat."; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Done { get; private set; }

    /// <summary>
    /// Whether Claude Desktop is on this computer and its configuration has ModShelf's server, as it would be added.
    /// </summary>
    [ObservableProperty]
    public partial string ClaudeDesktopNote { get; private set; } = string.Empty;

    /// <summary>
    /// Whether ModShelf's server can be added to Claude Desktop: the command line is beside the app, at a path that lasts,
    /// and Claude Desktop is on this computer.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToClaudeDesktopCommand))]
    public partial bool CanAddToClaudeDesktop { get; private set; }

    /// <summary>
    /// The command an agent's app starts, such as its path; or why there's none.
    /// </summary>
    public string CommandNote => _server.Command is { } command ? command.Path : Strings.AgentsNoCommand;

    public void Dispose()
    {
        _server.StateChanged -= OnStateChanged;
        _navigation.Navigated -= OnNavigated;
    }

    /// <summary>
    /// Reads the settings and shows them, serves the address or stops serving it as they say, and looks whether Claude
    /// Desktop has ModShelf's server. The app runs this as it starts. Runs on the UI thread.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        AppSettings stored;
        try
        {
            stored = await _settings.GetAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            // The Settings screen says why; agents stay off meanwhile.
            stored = new AppSettings();
        }

        Show(stored);
        await UserCommands.RunInPlaceAsync(
            () => _server.ApplyAsync(IsEnabled, _port, cancellationToken),
            _ => null,
            problem => Problem = problem,
            exception => LogApplyFailed(_logger, exception),
            cancellationToken);
        await RefreshClaudeDesktopAsync(cancellationToken);
    }

    /// <summary>
    /// The last look at Claude Desktop's configuration, for tests to wait on; it never throws.
    /// </summary>
    internal Task Refreshing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The last change of the toggle, saved and applied once this is done, for tests to wait on; it never throws.
    /// </summary>
    internal Task Changing { get; private set; } = Task.CompletedTask;

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_showing)
        {
            Changing = ChangeAsync(() => _settings.SetAgentsEnabledAsync(value ? true : null, CancellationToken.None), CancellationToken.None);
        }
    }

    /// <summary>
    /// Saves the port typed, and serves the address there.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSavePort))]
    private Task SavePortAsync(CancellationToken cancellationToken)
    {
        if (!TryReadPort(out int port))
        {
            Problem = Text.Format(Strings.AgentsPortInvalid, AppSettings.MinAgentsPort, AppSettings.MaxAgentsPort);
            return Task.CompletedTask;
        }

        return ChangeAsync(
            async () =>
            {
                await _settings.SetAgentsPortAsync(port == AppSettings.DefaultAgentsPort ? null : port, cancellationToken);
                _port = port;
            },
            cancellationToken);
    }

    private bool CanSavePort() => TryReadPort(out int port) && port != _port;

    /// <summary>
    /// Puts a prompt on the clipboard that asks an agent to add ModShelf's server to its app's configuration.
    /// </summary>
    [RelayCommand]
    private Task CopyPromptAsync(CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                await _clipboard.SetTextAsync(AgentSnippets.Prompt(_server.Command, AgentAddress.Of(_port)), cancellationToken);
                Problem = null;
                Done = IsEnabled ? Strings.AgentsPromptCopied : Strings.AgentsPromptCopiedWhileOff;
            },
            _ => null,
            problem => Problem = problem,
            exception => LogCopyFailed(_logger, exception),
            cancellationToken);

    /// <summary>
    /// Shows the server's details, for configuring an agent's app by hand.
    /// </summary>
    [RelayCommand]
    private Task ShowDetailsAsync(CancellationToken cancellationToken)
        => _dialogs.ShowAgentDetailsAsync(new AgentDetailsViewModel(_server.Command, AgentAddress.Of(_port), Status, _clipboard), cancellationToken);

    /// <summary>
    /// Adds ModShelf's server to Claude Desktop's configuration, after asking, keeping its other servers.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddToClaudeDesktop))]
    private Task AddToClaudeDesktopAsync(CancellationToken cancellationToken)
        => UserCommands.RunAsync(
            async () =>
            {
                if (_server.Command is not { } command)
                {
                    return;
                }

                ClaudeDesktopServer found;
                try
                {
                    found = await _claudeDesktop.ReadAsync(command, cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    await _dialogs.ShowMessageAsync(Strings.AgentsClaudeDesktopCantTitle, exception.Message, cancellationToken);
                    return;
                }

                switch (found)
                {
                    case ClaudeDesktopServer.NotFound:
                        await _dialogs.ShowMessageAsync(Strings.AgentsClaudeDesktopCantTitle, Strings.AgentsClaudeDesktopNotFound, cancellationToken);
                        await ReadClaudeDesktopAsync(cancellationToken);
                        return;
                    case ClaudeDesktopServer.Same:
                        await _dialogs.ShowMessageAsync(Strings.AgentsClaudeDesktopSameTitle, Strings.AgentsClaudeDesktopSameMessage, cancellationToken);
                        await ReadClaudeDesktopAsync(cancellationToken);
                        return;
                }

                bool replacing = found == ClaudeDesktopServer.Different;
                if (!await _dialogs.ConfirmAsync(
                    replacing ? Strings.AgentsClaudeDesktopReplaceTitle : Strings.AgentsClaudeDesktopAddTitle,
                    Text.Format(replacing ? Strings.AgentsClaudeDesktopReplaceMessage : Strings.AgentsClaudeDesktopAddMessage, _claudeDesktop.FilePath, _claudeDesktop.BackupPath),
                    replacing ? Strings.AgentsClaudeDesktopReplaceConfirm : Strings.AgentsClaudeDesktopAddConfirm,
                    cancellationToken))
                {
                    return;
                }

                try
                {
                    await _claudeDesktop.AddAsync(command, cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    await _dialogs.ShowMessageAsync(Strings.AgentsClaudeDesktopCantTitle, exception.Message, cancellationToken);
                    return;
                }

                LogAddedToClaudeDesktop(_logger);
                await ReadClaudeDesktopAsync(cancellationToken);
                await _dialogs.ShowMessageAsync(
                    Strings.AgentsClaudeDesktopAddedTitle,
                    IsEnabled ? Strings.AgentsClaudeDesktopAdded : Strings.AgentsClaudeDesktopAddedWhileOff,
                    cancellationToken);
            },
            _dialogs,
            exception => LogAddFailed(_logger, exception),
            cancellationToken);

    /// <summary>
    /// What <paramref name="state"/> says to the user.
    /// </summary>
    private static string Describe(AgentServerState state) => state.Status switch
    {
        AgentServerStatus.Starting => Strings.AgentsStatusStarting,
        AgentServerStatus.Running => Text.Format(Strings.AgentsStatusRunning, state.Address),
        AgentServerStatus.PortInUse => Text.Format(Strings.AgentsStatusPortInUse, state.Port),
        AgentServerStatus.NoCommand => Strings.AgentsStatusNoCommand,
        AgentServerStatus.Failed when state.ExitCode is { } code => Text.Format(Strings.AgentsStatusEnded, code),
        AgentServerStatus.Failed => Strings.AgentsStatusSlow,
        _ => Strings.AgentsStatusOff,
    };

    private bool TryReadPort(out int port)
        => int.TryParse(PortText.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out port)
            && port is >= AppSettings.MinAgentsPort and <= AppSettings.MaxAgentsPort;

    private void Show(AppSettings stored)
    {
        _showing = true;
        try
        {
            IsEnabled = stored.AgentsEnabled ?? false;
            _port = stored.AgentsPort ?? AppSettings.DefaultAgentsPort;
            PortText = _port.ToString(CultureInfo.CurrentCulture);
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>
    /// Saves a change, then serves the address as the settings now say; a change that can't be saved shows them as stored.
    /// </summary>
    private Task ChangeAsync(Func<Task> save, CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                Done = null;
                try
                {
                    await save();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    LogSaveFailed(_logger, exception);
                    Problem = Text.Format(Strings.AgentsCantSave, exception.Message);
                    Show(await _settings.GetAsync(cancellationToken));
                    return;
                }

                Problem = null;
                PortText = _port.ToString(CultureInfo.CurrentCulture);
                SavePortCommand.NotifyCanExecuteChanged();
                await _server.ApplyAsync(IsEnabled, _port, cancellationToken);
            },
            exception => exception is IOException or UnauthorizedAccessException or InvalidDataException ? Text.Format(Strings.AgentsCantSave, exception.Message) : null,
            problem => Problem = problem,
            exception => LogApplyFailed(_logger, exception),
            cancellationToken);

    /// <summary>
    /// Looks again whether Claude Desktop has ModShelf's server, saying anything unexpected in its note.
    /// </summary>
    private Task RefreshClaudeDesktopAsync(CancellationToken cancellationToken)
        => Refreshing = UserCommands.RunInPlaceAsync(
            () => ReadClaudeDesktopAsync(cancellationToken),
            _ => null,
            problem => ClaudeDesktopNote = problem,
            exception => LogReadFailed(_logger, exception),
            cancellationToken);

    private async Task ReadClaudeDesktopAsync(CancellationToken cancellationToken)
    {
        if (_server.Command is not { } command)
        {
            ClaudeDesktopNote = Strings.AgentsClaudeDesktopNoCommand;
            CanAddToClaudeDesktop = false;
            return;
        }

        ClaudeDesktopServer found;
        try
        {
            found = await _claudeDesktop.ReadAsync(command, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ClaudeDesktopNote = Text.Format(Strings.AgentsClaudeDesktopUnreadable, exception.Message);
            CanAddToClaudeDesktop = false;
            return;
        }

        ClaudeDesktopNote = found switch
        {
            ClaudeDesktopServer.NotFound => Strings.AgentsClaudeDesktopNotFound,
            ClaudeDesktopServer.Same => Strings.AgentsClaudeDesktopHasIt,
            ClaudeDesktopServer.Different => Strings.AgentsClaudeDesktopHasOther,
            _ => Strings.AgentsClaudeDesktopHasntIt,
        };
        CanAddToClaudeDesktop = found is ClaudeDesktopServer.Absent or ClaudeDesktopServer.Different;
    }

    /// <summary>
    /// Claude Desktop can be installed, or its configuration changed, while ModShelf runs: shown again, the Settings screen
    /// looks again.
    /// </summary>
    private void OnNavigated(object? sender, Screen screen)
    {
        if (screen == Screen.Settings)
        {
            _uiThread.Post(() => _ = RefreshClaudeDesktopAsync(CancellationToken.None));
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
        => _uiThread.Post(() =>
        {
            AgentServerState state = _server.State;
            Status = Describe(state);
            HasStatusProblem = state.Status is AgentServerStatus.PortInUse or AgentServerStatus.NoCommand or AgentServerStatus.Failed;
        });
}
