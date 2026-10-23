using System.Globalization;

namespace ModShelf.Testing;

/// <summary>
/// A fresh, empty folder under the system temp folder, deleted again on dispose.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModShelf.Tests", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on Windows; the system cleans its temp folder eventually.
        }
    }
}
