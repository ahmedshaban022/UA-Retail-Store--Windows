using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// Creates the context for design-time tooling such as <c>dotnet ef migrations</c>. The connection
/// string is irrelevant at design time; the model is what matters.
/// </summary>
public sealed class StoreFlowDbContextFactory : IDesignTimeDbContextFactory<StoreFlowDbContext>
{
    /// <summary>The design-time connection string.</summary>
    private const string DesignTimeConnectionString = "Data Source=storeflow-design.db";

    /// <inheritdoc />
    public StoreFlowDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<StoreFlowDbContext> builder = new DbContextOptionsBuilder<StoreFlowDbContext>();

        builder.UseSqlite(DesignTimeConnectionString, options =>
            options.MigrationsAssembly(typeof(StoreFlowDbContext).Assembly.FullName));

        return new StoreFlowDbContext(builder.Options);
    }
}
