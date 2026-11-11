using ModShelf.Application.Settings.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A Downloads folder under the system's temp folder, which nothing creates.
/// </summary>
internal sealed class FixedUserFolders : IUserFolders
{
    public string Downloads { get; } = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Downloads");
}
