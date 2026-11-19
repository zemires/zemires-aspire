
var builder = DistributedApplication.CreateBuilder(args);

var roach = builder.AddCockroachDB("roach")
    .WithDataVolume();

var db = roach.AddDatabase("mydb");

var api = builder.AddDotnetProject("api", "../CockroachDBTest.ApiService/CockroachDBTest.ApiService.csproj")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
