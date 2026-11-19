using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Zemires.Aspire.Hosting.CockroachDB.Tests;

// Starts a CockroachDB container via DistributedApplicationTestingBuilder, which needs a container runtime.
[Trait("Category", "Integration")]
public class CockroachDBIntegrationTests
{
    [Fact]
    public async Task CockroachDB_StartsSecure_AndCreatesDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var builder = DistributedApplicationTestingBuilder.Create();
        var db = builder.AddCockroachDB("roach").AddDatabase("db", "appdb");

        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await app.Services.GetRequiredService<ResourceNotificationService>()
            .WaitForResourceHealthyAsync(db.Resource.Name, cancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);

        var connectionString = await db.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT current_database(), current_user", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));
        Assert.Equal("appdb", reader.GetString(0));
        Assert.Equal("cockroach", reader.GetString(1));
    }
}
