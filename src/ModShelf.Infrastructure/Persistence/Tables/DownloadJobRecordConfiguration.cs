using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="DownloadJobRecord"/> to the <c>DownloadJobs</c> table.
/// </summary>
/// <remarks>
/// Enums are stored as their numbers, which the domain promises never to renumber. Text columns compare
/// case-sensitively, SQLite's default, because source keys and file names are case-sensitive on some systems.
/// </remarks>
internal sealed class DownloadJobRecordConfiguration : IEntityTypeConfiguration<DownloadJobRecord>
{
    public void Configure(EntityTypeBuilder<DownloadJobRecord> builder)
    {
        builder.ToTable("DownloadJobs");

        builder.HasKey(record => record.Sequence);
        builder.Property(record => record.Sequence).ValueGeneratedOnAdd();

        builder.HasIndex(record => record.Id).IsUnique();
    }
}
