using System.Xml.Linq;

var builder = DistributedApplication.CreateBuilder(args);

var staticToken = builder.AddParameter("directus-static-token", true); // generated in Directus Admin UI.

var userName = builder.AddParameter("userName", "admin@local.dev");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();
var db = postgres.AddDatabase("db");

//var sql = builder.AddSqlServer("sql")
//    .WithDataVolume();
//var db = sql.AddDatabase("db");

var directus = builder.AddDirectus("directus", port: 8055)
    .WithUserName(userName)
    //.WithSqlite3Database()
    .WithPostgresDatabase(db)
    //.WithSqlServerDatabase(db)
    .WithDataBindMount("./.data");
    //.WithDataVolume();

var api = builder.AddDotnetProject("api", "../DirectusApp.ApiService/DirectusApp.ApiService.csproj")
    .WithReference(directus)
    .WaitFor(directus);

//var web = builder.AddDotnetProject("web", "../DirectusApp.Web/DirectusApp.Web.csproj")
//    .WithReference(directus)
//    .WaitFor(directus);

#pragma warning disable ASPIREJAVASCRIPT001
var frontend = builder.AddNextJsApp("frontend", "../DirectusApp.Frontend")
    .WithPnpm()
    .WithEnvironment("DIRECTUS_STATIC_TOKEN", staticToken)
    .WithReference(directus)
    .WaitFor(directus);

#pragma warning restore ASPIREJAVASCRIPT001

builder.Build().Run();
