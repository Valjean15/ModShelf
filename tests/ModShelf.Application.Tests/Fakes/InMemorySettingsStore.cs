using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps the settings in memory, and counts how often they're saved.
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new();

    public int Saves { get; private set; }

    public string Location => "memory";

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        Saves++;
        return Task.CompletedTask;
    }
}
