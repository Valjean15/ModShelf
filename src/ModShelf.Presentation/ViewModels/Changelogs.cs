using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The mods' changelogs, read from their sites once per mod, for the updates on the Updates screen. A read that fails is
/// forgotten, so asking again asks the site again.
/// </summary>
internal sealed class Changelogs(ISourceRegistry sources)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ModRef, Task<IReadOnlyList<ModChangelog>>> _read = [];

    /// <returns>The mod's changelog, each version's changes; or null when its site gives no changelogs.</returns>
    public Task<IReadOnlyList<ModChangelog>>? ReadAsync(ModRef mod, CancellationToken cancellationToken)
    {
        if (sources.Find<IModCatalog>(mod.Game.Source) is not { } catalog)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_read.TryGetValue(mod, out Task<IReadOnlyList<ModChangelog>>? read))
            {
                read = catalog.GetChangelogsAsync(mod, cancellationToken);
                _read.Add(mod, read);
                _ = ForgetIfFailedAsync(mod, read);
            }

            return read;
        }
    }

    private async Task ForgetIfFailedAsync(ModRef mod, Task<IReadOnlyList<ModChangelog>> read)
    {
        try
        {
            await read;
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _read.Remove(mod);
            }
        }
    }
}
