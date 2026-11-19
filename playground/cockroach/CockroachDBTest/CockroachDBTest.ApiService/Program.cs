using CockroachDBTest.ApiService.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<BloggingContext>("mydb", configureDbContextOptions: options =>
{
    options.UseNpgsql(ngpsqlOptions =>
    {
        ngpsqlOptions.SetPostgresVersion(13, 0);
    });
});

var app = builder.Build();

// EF Core migrations fail on CockroachDB (the history table lock is not supported), and EnsureCreated()
// skips the schema because CockroachDB's internal tables look like existing tables. Create the tables directly.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BloggingContext>();
    try
    {
        dbContext.GetService<IRelationalDatabaseCreator>().CreateTables();
    }
    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.DuplicateTable)
    {
        // Schema already created on a previous start.
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/test", async (BloggingContext dbContext) =>
{
    dbContext.Blogs.Add(new Blog { Url = "https://example.com" });
    await dbContext.SaveChangesAsync();

    return "OK";
})
.WithName("GetTest");

app.Run();

