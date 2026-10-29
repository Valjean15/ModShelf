using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// Settings that load and save as the test says, such as by throwing what no command expects, or by waiting for
/// Ctrl+C. Without a way to save, saving does nothing.
/// </summary>
internal sealed class ScriptedSettingsStore(
    Func<CancellationToken, Task<AppSettings>> load,
    Func<AppSettings, CancellationToken, Task>? save = null) : ISettingsStore
{
    public string Location => "scripted";

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => load(cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        => save?.Invoke(settings, cancellationToken) ?? Task.CompletedTask;
}
