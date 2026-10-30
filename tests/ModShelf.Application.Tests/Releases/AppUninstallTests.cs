using ModShelf.Application.Links;
using ModShelf.Application.Releases;
using ModShelf.Application.Releases.Ports;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Releases;

/// <summary>
/// What the uninstaller puts back through ModShelf: the sites' links, and ModShelf's own registrations.
/// </summary>
public sealed class AppUninstallTests
{
    private readonly FakeModSite _site = new();
    private readonly OtherSite _other = new();
    private readonly MemorySchemeRegistration _registration = new();
    private readonly RecordingRegistrations _registrations = new();
    private readonly RecordingLogger<AppUninstall> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GivesBackTheLinksModShelfOpensAndRemovesItsOwnRegistrations()
    {
        _registration.States["fakemods"] = new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex");

        await Create().CleanUpAsync(Cancellation);

        Assert.Equal(["Unregister fakemods"], _registration.Calls);
        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), _registration.States["fakemods"]);
        Assert.Equal(1, _registrations.Removed);
    }

    [Fact]
    public async Task GivesBackTheLinksModShelfIsRegisteredForBehindTheUsersChoice()
    {
        _registration.States["othermods"] = new SchemeState(SchemeHandlerKind.OtherApp, "Vortex", "Mod Organizer 2", IsModShelfBehind: true);

        await Create().CleanUpAsync(Cancellation);

        Assert.Equal(["Unregister othermods"], _registration.Calls);
    }

    [Theory]
    [InlineData(SchemeHandlerKind.None)]
    [InlineData(SchemeHandlerKind.OtherApp)]
    [InlineData(SchemeHandlerKind.Unavailable)]
    public async Task LeavesLinksModShelfDoesntOpen(SchemeHandlerKind kind)
    {
        // Another app's, or another ModShelf's, such as one on a checklist's library, which gives them back itself.
        _registration.States["fakemods"] = new SchemeState(kind, kind == SchemeHandlerKind.OtherApp ? "Another ModShelf" : null);

        await Create().CleanUpAsync(Cancellation);

        Assert.Empty(_registration.Calls);
        Assert.Equal(1, _registrations.Removed);
    }

    [Fact]
    public async Task ALinkThatCantBeGivenBackLeavesTheRestToRun()
    {
        _registration.States["fakemods"] = new SchemeState(SchemeHandlerKind.ModShelf);
        _registration.States["othermods"] = new SchemeState(SchemeHandlerKind.ModShelf);
        _registration.Failure = new UnauthorizedAccessException("The registry refused.");

        await Create().CleanUpAsync(Cancellation);

        Assert.Equal(["Unregister fakemods", "Unregister othermods"], _registration.Calls);
        Assert.Equal(SchemeState.None, _registration.States["othermods"]);
        Assert.Equal(1, _registrations.Removed);
        Assert.StartsWith("Warning: Uninstalling, ModShelf couldn't give fake-mods's fakemods:// links back", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationsThatCantBeRemovedAreLogged()
    {
        _registrations.Failure = new IOException("The registry refused.");

        await Create().CleanUpAsync(Cancellation);

        Assert.StartsWith("Warning: Uninstalling, ModShelf couldn't remove what it registered for itself", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinksThatCantBeReadStillLeaveItsRegistrationsRemoved()
    {
        await Create(new FailingLinkHandlers(new UnauthorizedAccessException("The registry refused."))).CleanUpAsync(Cancellation);

        Assert.Equal(1, _registrations.Removed);
        Assert.StartsWith("Warning: Uninstalling, ModShelf couldn't tell which app opens the sites' links", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsWhenCancelled()
    {
        _registration.States["fakemods"] = new SchemeState(SchemeHandlerKind.ModShelf);
        _registration.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Create().CleanUpAsync(Cancellation));

        Assert.Equal(0, _registrations.Removed);
    }

    [Fact]
    public async Task StopsWhenCancelledReadingTheLinks()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(new FailingLinkHandlers(new OperationCanceledException())).CleanUpAsync(Cancellation));

        Assert.Equal(0, _registrations.Removed);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task StopsWhenCancelledRemovingItsRegistrations()
    {
        _registrations.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Create().CleanUpAsync(Cancellation));

        Assert.Empty(_logger.Entries);
    }

    private AppUninstall Create(ILinkHandlers? links = null)
        => new(
            links ?? new LinkHandlers(new SourceRegistry([_site, _other]), _registration, new RecordingLogger<LinkHandlers>()),
            _registrations,
            _logger);

    private sealed class RecordingRegistrations : IAppRegistrations
    {
        public int Removed { get; private set; }

        public Exception? Failure { get; set; }

        public Task RemoveAsync(CancellationToken cancellationToken)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            Removed++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A second site, which hands downloads over with <c>othermods://</c> links.
    /// </summary>
    private sealed class OtherSite : IHandoffScheme
    {
        public SourceId Id { get; } = new("other-mods");

        public string DisplayName => "Other mods";

        public string Scheme => "othermods";
    }

    private sealed class FailingLinkHandlers(Exception failure) : ILinkHandlers
    {
        public Task<IReadOnlyList<LinkHandler>> ListAsync(CancellationToken cancellationToken) => throw failure;

        public Task<LinkHandler> TakeAsync(SourceId source, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LinkHandler> GiveBackAsync(SourceId source, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
