using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The library database, with each change first doing what the test says, such as failing as a locked database does,
/// or waiting for Ctrl+C.
/// </summary>
internal sealed class ScriptedLibraryRepository(ILibraryRepository library, Func<CancellationToken, Task> beforeUpdate) : ILibraryRepository
{
    public Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken) => library.ListAsync(cancellationToken);

    public Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken) => library.FindAsync(modRef, cancellationToken);

    public async Task<LibraryChange> UpdateAsync(ModRef modRef, Func<LibraryMod?, LibraryMod?> change, CancellationToken cancellationToken)
    {
        await beforeUpdate(cancellationToken).ConfigureAwait(false);
        return await library.UpdateAsync(modRef, change, cancellationToken).ConfigureAwait(false);
    }
}
