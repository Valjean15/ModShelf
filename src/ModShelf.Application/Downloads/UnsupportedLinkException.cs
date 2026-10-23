namespace ModShelf.Application.Downloads;

/// <summary>
/// No registered source recognizes a link, so it can't be downloaded.
/// </summary>
public sealed class UnsupportedLinkException : Exception
{
    public UnsupportedLinkException()
        : this("No source can download this link.")
    {
    }

    public UnsupportedLinkException(string message)
        : base(message)
    {
    }

    public UnsupportedLinkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
