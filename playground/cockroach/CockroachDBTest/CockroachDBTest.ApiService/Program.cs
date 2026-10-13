using CockroachDBTest.ApiService.Model;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

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
    options.UseCockroach();
});

var app = builder.Build();

// damn, cochroach does not support EFCore.
using var scope = app.Services.CreateScope();
using var dbContext = scope.ServiceProvider.GetRequiredService<BloggingContext>();
dbContext.Database.EnsureCreated();
dbContext.Database.Migrate();

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

