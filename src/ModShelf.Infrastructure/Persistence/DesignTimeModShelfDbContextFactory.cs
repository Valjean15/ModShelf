using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Lets the dotnet-ef tool build the model when creating a migration. The app never uses it, and
/// creating migrations never opens the database it names.
/// </summary>
internal sealed class DesignTimeModShelfDbContextFactory : IDesignTimeDbContextFactory<ModShelfDbContext>
{
    public ModShelfDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ModShelfDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
