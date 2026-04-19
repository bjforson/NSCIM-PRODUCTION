using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NickHR.Infrastructure.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// </summary>
public class NickHRDbContextFactory : IDesignTimeDbContextFactory<NickHRDbContext>
{
    public NickHRDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=NickHR;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<NickHRDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new NickHRDbContext(optionsBuilder.Options);
    }
}
