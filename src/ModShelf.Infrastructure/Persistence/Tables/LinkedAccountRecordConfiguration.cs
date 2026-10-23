using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="LinkedAccountRecord"/> to the <c>LinkedAccounts</c> table.
/// </summary>
/// <remarks>
/// The kind is stored as its number, which Sources.Abstractions promises never to renumber. The source ID compares
/// case-sensitively, like every source ID.
/// </remarks>
internal sealed class LinkedAccountRecordConfiguration : IEntityTypeConfiguration<LinkedAccountRecord>
{
    public void Configure(EntityTypeBuilder<LinkedAccountRecord> builder)
    {
        builder.ToTable("LinkedAccounts");

        builder.HasKey(record => record.SourceId);
    }
}
