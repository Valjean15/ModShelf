using ModShelf.Application.Diagnostics;
using ModShelf.Application.Diagnostics.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A diagnostics report and the app's folders, as the test sets them.
/// </summary>
internal sealed class FixedDiagnostics : IDiagnostics, IAppFolders
{
    public string Report { get; set; } = "ModShelf 0.9.0 (a60d76b)\n";

    public string DataFolder { get; set; } = Path.Combine(Path.GetTempPath(), "ModShelf");

    public string LogFolder { get; set; } = Path.Combine(Path.GetTempPath(), "ModShelf", "logs");

    public Task<string> DescribeAsync(CancellationToken cancellationToken) => Task.FromResult(Report);
}
