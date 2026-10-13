using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var roach = builder.AddCockroachDB("roach")
    .WithDataVolume();

var db = roach.AddDatabase("mydb");

var api = builder.AddProject<CockroachDBTest_ApiService>("api")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
