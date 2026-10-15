using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = false)
    .WithTools<WeatherTools>();

builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapOpenApi();

app.UseHttpsRedirection();

app.MapGet("/api/add", (double a, double b) =>
{
    return Results.Ok(new { result = a + b });
})
.WithName("GetAdd")
.Produces(200, typeof(object));

app.MapMcp();

await app.RunAsync();
