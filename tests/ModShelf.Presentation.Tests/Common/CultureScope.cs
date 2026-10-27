using System.Globalization;

namespace ModShelf.Presentation.Tests.Common;

/// <summary>
/// Runs the code after it in a culture, such as en-US for "1.50 MB", until disposed: numbers are written the way the
/// culture writes them, whatever the computer's own culture.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentCulture;

    public CultureScope(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
    }

    public void Dispose() => CultureInfo.CurrentCulture = _before;
}
