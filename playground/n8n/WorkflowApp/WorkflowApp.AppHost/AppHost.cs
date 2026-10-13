var builder = DistributedApplication.CreateBuilder(args);

var ownerPassword = builder.AddParameter("owner-password", true);
var licenseKey = builder.AddParameter("license-key", true);
var adminApiKey = builder.AddParameter("admin-api-key", true); // value needs to be created manually in n8n

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var db = postgres.AddDatabase("n8n-db");

var redis = builder.AddRedis("redis")
    .WithDataVolume();

//var ollama = builder.AddOllama("ollama"):
var ollama = builder.AddOllamaLocal("ollama");
//var model_gemma = ollama.AddModel("gemma3");
var model_gwen = ollama.AddModel("qwen3.5:0.8b"); // small
//var model_gwenembed = ollama.AddModel("qwen3-embedding:0.6b");
var model_embedding = ollama.AddModel("embeddinggemma");

//var milvus = builder.AddMilvus("milvus")
//    .WithDataVolume();
//var ragdb = milvus.AddDatabase("ragdb");

var credentials = ReferenceExpression.Create($"{{" +
    $"\"ollamaApi\":{{\"baseUrl\":\"{ollama.Resource.PrimaryEndpoint}\"}}" +
    $",\"redis\":{{ \"host\":\"{redis.Resource.Host}\", \"port\":{redis.Resource.Port}, \"ssl\":true, \"password\":\"{redis.Resource.PasswordParameter}\" }}" +
    //$",\"milvusApi\":{{ \"baseUrl\":\"{milvus.Resource.UriExpression}\", \"password\":\"{milvus.Resource.ApiKeyParameter}\", \"username\":\"root\" }}" +
    $"}}");

var n8n = builder.AddN8n("n8n", port: 5678)
    .WithDataBindMount("./.n8n_data")
    .WithPostgresDatabase(db)
    .WithQueueMode(redis)
    .WithTimeZone("Europe/Zurich")
    .WithInstanceOwner("admin@dev.local", "Admin", "Local", ownerPassword)
    .WithLicenseKey(licenseKey)
    .WithOtlpExporter()
    .WithEnvironment("N8N_LOG_LEVEL", "debug").WithEnvironment("N8N_LOG_OUTPUT", "console,file")
    .WithEnvironment("CREDENTIALS_OVERWRITE_DATA", credentials);

var worker = n8n.AddWorker("worker", port: 5679)
    .WithPostgresDatabase(db)
    .WithQueueMode(redis)
    .WithTimeZone("Europe/Zurich")
    .WithOtlpExporter()
    .WithEnvironment("CREDENTIALS_OVERWRITE_DATA", credentials);

worker.AddTaskRunner("runner");

var api = builder.AddProject<Projects.WorkflowApp_ApiService>("apiservice")
    .WithReference(n8n)
    .WaitFor(n8n)
    .WithEnvironment("Aspire__n8n__Client__ApiKey", adminApiKey);

worker.WithReference(api);

var mcp = builder.AddProject<Projects.WorkflowApp_McpService>("mcpservice");
n8n.WithReference(mcp);
worker.WithReference(mcp);

builder.Build().Run();
