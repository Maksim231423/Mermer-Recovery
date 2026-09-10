using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mermer.Data.Postgres;

public class MermerDbContextFactory : IDesignTimeDbContextFactory<MermerDbContext>
{
    public MermerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MermerDbContext>();

        optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Database=mermer_creation;Username=mermer_creation;Password=mermer_strong_password_123");

        return new MermerDbContext(optionsBuilder.Options);
    }
}