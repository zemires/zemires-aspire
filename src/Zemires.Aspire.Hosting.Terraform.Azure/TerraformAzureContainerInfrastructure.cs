using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

namespace Aspire.Hosting;

/// <summary>
/// Represents the infrastructure for Azure Container Apps within the Aspire Hosting environment.
/// </summary>
internal sealed class TerraformAzureContainerInfrastructure : IDistributedApplicationEventingSubscriber
{
    private async Task OnBeforeStartAsync(BeforeStartEvent @event, CancellationToken cancellationToken = default)
    {
        var caes = @event.Model.Resources.OfType<TerraformAzureEnvironmentResource>().ToArray();

        if (caes.Length == 0)
        {
            return;
        }

        foreach (var environment in caes)
        {
            foreach (var r in @event.Model.GetComputeResources())
            {
                // Skip resources that are explicitly targeted to a different compute environment
                var resourceComputeEnvironment = r.GetComputeEnvironment();
                if (resourceComputeEnvironment is not null && resourceComputeEnvironment != environment)
                {
                    continue;
                }

                // Capture information about the container registry used by the
                // container app environment in the deployment target information
                // associated with each compute resource that needs an image
                r.Annotations.Add(new DeploymentTargetAnnotation(environment)
                {
                    ContainerRegistry = environment,
                    ComputeEnvironment = environment
                });
            }
        }
    }

    public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        if (!executionContext.IsRunMode)
        {
            eventing.Subscribe<BeforeStartEvent>(OnBeforeStartAsync);
        }

        return Task.CompletedTask;
    }
}
