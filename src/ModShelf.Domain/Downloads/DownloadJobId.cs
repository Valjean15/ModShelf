using System.Globalization;

namespace ModShelf.Domain.Downloads;

/// <summary>
/// Identifies a <see cref="DownloadJob"/>.
/// </summary>
public sealed record DownloadJobId
{
    public DownloadJobId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A download job ID can't be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static DownloadJobId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
