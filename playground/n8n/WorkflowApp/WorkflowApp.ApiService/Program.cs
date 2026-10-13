using Microsoft.Kiota.Abstractions.Serialization;
using Zemires.N8n.Api;
using Zemires.N8n.Api.Models;
using Zemires.N8n.Api.Executions;
using Zemires.N8n.Api.Workflows.Item.Activate;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

var n8nConfig = builder.Configuration.GetSection("N8n");

builder.Services.AddHttpClient<WorkflowClient>(httpClient =>
{
    httpClient.BaseAddress = new Uri(builder.Configuration.GetConnectionString("n8n"));
});

builder.AddN8nClient("n8n");

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/");
}

app.UseHttpsRedirection();

app.MapGet("/start", async (WorkflowClient client) =>
{
    await client.TestWorkflowWebhook();
});

app.MapGet("/testapi", async (N8nClient client, ILogger<Program> logger, CancellationToken ct) =>
{
    var wfs = await client.Workflows.GetAsync(cancellationToken: ct);
    foreach (var wf in wfs.Data)
    {
        logger.LogInformation("Workflow: {id} - {name}", wf.Id, wf.Name);

        var execs = await client.Executions.GetAsync((p) => 
        {
            p.QueryParameters.WorkflowId = wf.Id;
            p.QueryParameters.Status = GetStatusQueryParameterType.Running;
        }, ct);
        var exeIds = string.Join(",", execs.Data.Select(e => e.Id));

        logger.LogInformation("Workflow running: {id} - {execs}", wf.Id, exeIds);
    }
    return wfs.Data.Select(p => new { p.Id, p.Name, p.Active });
});

app.MapGet("/loadwf", async (N8nClient client, CancellationToken ct) =>
{
    using var fs = File.OpenRead("Workflow1.json");

    var wf = await KiotaJsonSerializer.DeserializeAsync<Workflow>(fs);
    var existingWf = await client.Workflows.GetAsync(p =>
    {
        p.QueryParameters.Name = wf.Name;
        p.QueryParameters.Active = Zemires.N8n.Api.Workflows.GetActiveQueryParameterType.True;
    });

    if (existingWf.Data.Count > 0)
    {
        var id = existingWf.Data.First().Id;
        var put = await client.Workflows[id].PutAsync(wf);
        return id;
    }

    fs.Position = 0;
    var wfCreate = await KiotaJsonSerializer.DeserializeAsync<WorkflowCreate>(fs);
    var result = await client.Workflows.PostAsync(wfCreate, cancellationToken: ct);
    await client.Workflows[result.Id].Activate.PostAsync(new ActivatePostRequestBody());
    return result.Id ?? "";
});

app.Run();

