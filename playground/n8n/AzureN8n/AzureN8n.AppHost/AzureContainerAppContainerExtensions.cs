using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;

namespace Aspire.Hosting;

public static class AzureContainerAppContainerExtensions
{
    public static IResourceBuilder<T> PublishAsAzureContainerAppSidecar<T, T2>(this IResourceBuilder<T> sidecarContainer, IResourceBuilder<T2> container, Action<ContainerAppContainer>? configure = null)
        where T : ContainerResource
        where T2 : ContainerResource
    {
        ArgumentNullException.ThrowIfNull(sidecarContainer);
        ArgumentNullException.ThrowIfNull(container);

        if (!sidecarContainer.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return sidecarContainer;
        }

        sidecarContainer
            .PublishAsAzureContainerApp((infra, app) => 
            {
                container.WithAnnotation(new ContainerAppContainerSidecarAnnotation(infra.GetProvisionableResources().ToList(), app), ResourceAnnotationMutationBehavior.Replace);
                foreach(var resource in infra.GetProvisionableResources().ToList())
                {
                    infra.Remove(resource); // remove all
                };
                infra.AspireResource.Parameters.Clear();
                infra.AspireResource.Parameters.Add("location", null); // is needed
            });

        _ = container
            .PublishAsAzureContainerApp((infra, app) =>
            {
                if (container.Resource.TryGetLastAnnotation<ContainerAppContainerSidecarAnnotation>(out var sidecarAnnotation))
                {
                    var sidecarContainerApp = sidecarAnnotation.SidecarContainerApp;
                    if (sidecarContainerApp == null)
                    {
                        return;
                    }

                    var sidecarContainer = sidecarContainerApp.Template.Containers.First();

                    if (configure != null && sidecarContainerApp != null)
                    {
                        configure.Invoke(sidecarContainer.Value!);
                    }

                    app.Template.Containers.Add(sidecarContainer);
                }
            })
            .WithRelationship(sidecarContainer, "Sidecar");

        // ensure order of azure publishing within model: sidecar must be created before container
        sidecarContainer.ApplicationBuilder.Eventing.Subscribe<BeforePublishEvent>((evt, ct) =>
        {
            var sidecarIndex = evt.Model.Resources.IndexOf(sidecarContainer.Resource);
            var containerIndex = evt.Model.Resources.IndexOf(container.Resource);

            evt.Model.Resources.Remove(sidecarContainer.Resource);
            evt.Model.Resources.Insert(containerIndex, sidecarContainer.Resource);

            return Task.CompletedTask;
        });

        sidecarContainer.WithPipelineStepFactory((context) => {
#pragma warning disable ASPIREPIPELINES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            return new PipelineStep()
            {
                Name = "prepare-sidecar-container-app-" + sidecarContainer.Resource.Name,
                Action = async (ctx) =>
                {
                    // merge annotations from sidecar to container
                    foreach (var annotation in sidecarContainer.Resource.Annotations.ToList())
                    {
                        if (annotation is EnvironmentCallbackAnnotation || annotation is ResourceRelationshipAnnotation)
                        {
                            container.Resource.Annotations.Add(annotation);
                        }
                    }
                },
                RequiredBySteps = [ WellKnownPipelineSteps.DeployPrereq, WellKnownPipelineSteps.PublishPrereq, "azure-prepare-resources" ],
                DependsOnSteps = [],
            };
#pragma warning restore ASPIREPIPELINES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        });

        return sidecarContainer;
    }
}
