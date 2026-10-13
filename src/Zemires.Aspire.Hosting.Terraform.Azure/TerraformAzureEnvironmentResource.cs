using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zemires.Aspire.Hosting.Terraform;

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

namespace Aspire.Hosting;

/// <summary>
/// A Terraform environment for Azure that also acts as the container registry for the resources deployed to it.
/// </summary>
public class TerraformAzureEnvironmentResource : TerraformEnvironmentResource, IContainerRegistry
{
    /// <summary>
    /// Gets a reference to the <c>container_registry_server</c> Terraform output (the registry login server).
    /// </summary>
    public TerraformOutputReference ContainerRegistryServer => new($"container_registry_server_{Name}", this);

    /// <summary>
    /// Gets a reference to the <c>container_registry_name</c> Terraform output.
    /// </summary>
    public TerraformOutputReference ContainerRegistryName => new($"container_registry_name_{Name}", this);

    /// <summary>
    /// Initializes a new <see cref="TerraformAzureEnvironmentResource"/> and registers its Azure deployment pipeline steps.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="templatesLocation">The directory or URL containing the Handlebars templates.</param>
    public TerraformAzureEnvironmentResource(string name, string templatesLocation) : base(name, templatesLocation)
    {
        Annotations.Add(new PipelineStepAnnotation((context) =>
        {
            var deployRegistry = new PipelineStep()
            {
                Name = "deploy-terraform-registry-" + name,
                Action = async context =>
                {
                    var apply = await context.ReportingStep.CreateTaskAsync(new MarkdownString("# Deploy registry"), context.CancellationToken);

                    var terraformTemplatePublisher = context.Services.GetRequiredService<TerraformExecutor>();
                    await terraformTemplatePublisher.Apply(context.CancellationToken, $"-target azurerm_container_registry.{name}");

                    await apply.CompleteAsync("Done");
                },
                RequiredBySteps = ["deploy-terraform-" + name],
                DependsOnSteps = ["publish-terraform-" + name, WellKnownPipelineSteps.DeployPrereq]
            };

            var readOutput = new PipelineStep()
            {
                Name = "read-terraform-output-" + name,
                Action = async context =>
                {
                    var terraformExecutor = context.Services.GetRequiredService<TerraformExecutor>();

                    context.Logger.LogInformation("Get outputs from state...");

                    var outputs = await terraformExecutor.GetOutputs(true, context.CancellationToken);

                    foreach (var output in outputs)
                    {
                        Outputs[output.Key] = output.Value;
                    }
                },
                RequiredBySteps = [WellKnownPipelineSteps.Push],
                DependsOnSteps = [WellKnownPipelineSteps.Publish, "deploy-terraform-registry-" + name]
            };

            var loginACR = new PipelineStep()
            {
                Name = "login-acr-" + name,
                Action = async context =>
                {
                    if (!Outputs.ContainsKey($"container_registry_server_{name}"))
                    {
                        throw new InvalidOperationException("Output 'container_registry_server_[name]' not found in Terraform outputs.");
                    }

                    var registryServer = Outputs[$"container_registry_server_{name}"];

                    // login acr via az
                    var exitCode = await ProcessExecutorHelper.ExecuteProcess(OperatingSystem.IsWindows() ? "C:\\Program Files\\Microsoft SDKs\\Azure\\CLI2\\wbin\\az.cmd" : "az", $"acr login -n {registryServer}", context.Logger, context.CancellationToken);

                    if (exitCode != 0)
                    {
                        context.Logger.LogError("Failed to login to registry {server}", registryServer);
                        return;
                    }
                },
                RequiredBySteps = [WellKnownPipelineSteps.PushPrereq],
                DependsOnSteps = [WellKnownPipelineSteps.Publish, "read-terraform-output-" + name, "deploy-terraform-registry-" + name]
            };

            // pseudo steps for azure resources run as container
            var createProvContext = new PipelineStep()
            {
                Name = "create-provisioning-context",
                Action = async context =>
                {
                },
                RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq]
            };

            var provAzBicep = new PipelineStep()
            {
                Name = "provision-azure-bicep-resources",
                Action = async context =>
                {
                },
                RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq]
            };

            return [deployRegistry, readOutput, loginACR, createProvContext, provAzBicep];
        }));
    }

    /// <summary>
    /// Gets the container registry endpoint, resolved from the <c>container_registry_server</c> Terraform output.
    /// </summary>
    public ReferenceExpression Endpoint => ReferenceExpression.Create($"{ContainerRegistryServer}");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{ContainerRegistryName}");
}
