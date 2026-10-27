using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps the settings in memory, and counts how often they're saved. The test can make loading them fail.
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new();

    public int Saves { get; private set; }

    /// <summary>
    /// What loading throws, while set.
    /// </summary>
    public Exception? Failure { get; set; }

    public string Location => "memory";

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        => Failure is null ? Task.FromResult(Settings) : Task.FromException<AppSettings>(Failure);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        Saves++;
        return Task.CompletedTask;
    }
}
