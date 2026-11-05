var builder = DistributedApplication.CreateBuilder(args);

var dbUserName = builder.AddParameter("db-username", "postgres");
var dbPassword = builder.AddParameter("db-password", "Postgres!123");

var postgres = builder.AddPostgres("postgres", dbUserName, dbPassword);
var db = postgres.AddDatabase("db");

var n8n = builder.AddN8n("n8n")
    .WithPostgresDatabase(db) // optional postgres database, if not provided n8n will use SQLite
    .WithInstanceOwner("admin@dev.local", "Admin", "Local") // password generated, parameter "n8n-instance-owner-password"
    .WithApiKey(); // API key for the public API, part of the connection string of "n8n"

builder.Build().Run();
