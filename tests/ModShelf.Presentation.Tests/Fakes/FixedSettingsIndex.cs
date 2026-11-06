using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The settings' parts as the test lists them.
/// </summary>
internal sealed class FixedSettingsIndex : ISettingsIndex
{
    public List<SettingsSearchEntry> Listed { get; } = [];

    public IReadOnlyList<SettingsSearchEntry> Entries => Listed;
}
