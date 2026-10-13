using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Qute.Directus;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddDirectusClient("directus");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/users", async (DirectusClient client, CancellationToken ct) =>
{
    var users = await client.Users.GetManyAsync(ct: ct);
    return users;
});

app.MapGet("/images", async (DirectusClient client, CancellationToken ct) =>
{
    var items = await client.Items.GetManyAsync("test", ct: ct);
    return items;
});

app.MapGet(pattern: "/images/{imageId}", async (HttpContext context, DirectusClient client, CancellationToken ct, string imageId) =>
{
    var stream = await client.Assets.GetAsync(imageId, ct: ct);
    await stream.CopyToAsync(context.Response.Body, ct);
});

app.Run();
