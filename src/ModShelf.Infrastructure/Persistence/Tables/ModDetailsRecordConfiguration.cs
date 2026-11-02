using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="ModDetailsRecord"/> to the <c>ModDetails</c> table, one row per mod, keyed by its reference. It
/// stands apart from the library's tables: a mod's details stay when it leaves the library, ready if it comes back.
/// </summary>
internal sealed class ModDetailsRecordConfiguration : IEntityTypeConfiguration<ModDetailsRecord>
{
    public void Configure(EntityTypeBuilder<ModDetailsRecord> builder)
    {
        builder.ToTable("ModDetails");

        builder.HasKey(record => new { record.SourceId, record.GameKey, record.ModId });
    }
}
