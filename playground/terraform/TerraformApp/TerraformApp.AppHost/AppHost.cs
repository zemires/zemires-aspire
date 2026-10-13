var builder = DistributedApplication.CreateBuilder(args);

var env = builder.AddTerraformEnvironment("env");

var tf = builder.AddTerraformTemplate("tf1", "tf1.tf.hbs")
    .WithParameter("len", "5");

var container1 = builder.AddContainer("container1", "mcr.microsoft.com/azuredocs/aci-helloworld:latest")
    .WithEnvironment("TEST", tf.GetOutput("result"));

builder.Build().Run();
