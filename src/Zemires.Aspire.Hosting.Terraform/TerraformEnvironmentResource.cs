using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zemires.Aspire.Hosting.Terraform;
using Zemires.Aspire.Hosting.Terraform.Models;

#pragma warning disable IDE0130
#pragma warning disable ASPIREPIPELINES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Aspire.Hosting;

/// <summary>
/// A compute environment that publishes the resources deployed to it as Terraform configuration and, on deploy, applies it.
/// </summary>
public class TerraformEnvironmentResource : TerraformTemplateResource, ITerraformResourceWithOutputs, IComputeEnvironmentResource
{
    /// <summary>
    /// Gets or sets the directory or URL containing the Handlebars templates.
    /// </summary>
    public string TemplatesLocation { get; set; }

    /// <summary>
    /// Initializes a new <see cref="TerraformEnvironmentResource"/> and registers its prepare, publish and deploy pipeline steps.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="templatesLocation">The directory or URL containing the Handlebars templates.</param>
    public TerraformEnvironmentResource(string name, string templatesLocation) : base(name)
    {
        TemplatesLocation = templatesLocation;

        Annotations.Add(new PipelineStepAnnotation((context) =>
        {
            var prepare = new PipelineStep()
            {
                Name = "prepare-terraform-" + name,
                Action = async context =>
                {
                    var publisher = context.Services.GetRequiredService<ITerraformTemplatePublisher>();

                    var taskPrepare = await context.ReportingStep.CreateTaskAsync("Prepare");
                    await publisher.PrepareResources(context.Model);

                    await taskPrepare.CompleteAsync("Prepare done.");
                },
                RequiredBySteps = [WellKnownPipelineSteps.PublishPrereq],
                DependsOnSteps = [],
            };

            var publish = new PipelineStep()
            {
                Name = "publish-terraform-" + name,
                Action = async context =>
                {
                    var publisher = context.Services.GetRequiredService<ITerraformTemplatePublisher>();

                    var taskPrepare = await context.ReportingStep.CreateTaskAsync("Init publishing");

                    var templates = await publisher.GetTerraformTemplateAnnotations(context.Model); // TODO filter by env

                    await publisher.InitPublishing(TemplatesLocation, templates);

                    await taskPrepare.CompleteAsync("Init publish done.");

                    var taskPublish = await context.ReportingStep.CreateTaskAsync("Publish");
                    foreach (var template in templates)
                    {
                        await taskPublish.UpdateAsync($"Publishing {template.TemplateFile}...");
                        await publisher.PublishTemplateAsync(template);
                    }
                    await taskPublish.CompleteAsync("Publish done.");
                },
                RequiredBySteps = [WellKnownPipelineSteps.Publish],
                DependsOnSteps = [WellKnownPipelineSteps.PublishPrereq, "prepare-terraform-" + name]
            };

            var deploy = new PipelineStep()
            {
                Name = "deploy-terraform-" + name,
                Action = async context =>
                {
                    // get deployment tag
                    var deploymentTags = new Dictionary<string, string>();
                    foreach (var resource in context.Model.GetBuildResources())
                    {
#pragma warning disable ASPIREPIPELINES003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                        resource.TryGetLastAnnotation<ContainerImagePushOptionsCallbackAnnotation>(out var imagePush);

                        if (imagePush != null)
                        {
                            var pushOptions = new ContainerImagePushOptionsCallbackContext()
                            {
                                Resource = resource,
                                Options = new ContainerImagePushOptions()
                                {
                                    // Set default options here if needed
                                },
                                CancellationToken = context.CancellationToken
                            };
                            await imagePush.Callback(pushOptions);
                            deploymentTags.Add(resource.Name, pushOptions.Options.RemoteImageTag ?? "latest");
                        }
#pragma warning restore ASPIREPIPELINES003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    }

                    var tfVars = new Dictionary<string, string>() { { "image_tags", "{" + string.Join(",", deploymentTags.Select(kvp => $"{kvp.Key}:\"{kvp.Value}\"")) + "}" } };

                    var terraformTemplatePublisher = context.Services.GetRequiredService<TerraformExecutor>();
                    await terraformTemplatePublisher.Apply(context.CancellationToken, terraformVariables: tfVars);
                },
                RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                DependsOnSteps = ["publish-terraform-" + name, WellKnownPipelineSteps.DeployPrereq, WellKnownPipelineSteps.Push]
            };

            var destroy = new PipelineStep()
            {
                Name = "destroy-terraform-" + name,
                Action = async context =>
                {
                    var deploymentTags = new Dictionary<string, string>();
                    foreach (var resource in context.Model.GetBuildResources())
                            deploymentTags.Add(resource.Name, "none");
                    var tfVars = new Dictionary<string, string>() { { "image_tags", "{" + string.Join(",", deploymentTags.Select(kvp => $"{kvp.Key}:\"{kvp.Value}\"")) + "}" } };

                    var terraformTemplatePublisher = context.Services.GetRequiredService<TerraformExecutor>();
                    await terraformTemplatePublisher.Destroy(context.CancellationToken, terraformVariables: tfVars);
                },
                RequiredBySteps = [WellKnownPipelineSteps.Destroy],
                DependsOnSteps = ["publish-terraform-" + name, WellKnownPipelineSteps.DestroyPrereq]
            };

            return [prepare, publish, deploy, destroy];
        }));

        Annotations.Add(ManifestPublishingCallbackAnnotation.Ignore);
    }

    /// <summary>
    /// Gets the container registry referenced by this environment, if any.
    /// </summary>
    /// <returns>The referenced container registry, or <see langword="null"/> if none is configured.</returns>
    protected IContainerRegistry? GetContainerRegistry()
    {
        // Check for explicit container registry reference annotation
        if (this.TryGetLastAnnotation<ContainerRegistryReferenceAnnotation>(out var annotation))
        {
            return annotation.Registry;
        }

        // Fall back to default container registry
        return null;
    }
}

/// <summary>
/// Extension methods for adding Terraform templates to a <see cref="TerraformEnvironmentResource"/>.
/// </summary>
public static class TerraformEnvironmentResourceExtensions
{
    /// <summary>
    /// Adds a specific Terraform template resource to the distributed application builder.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name.</param>
    /// <param name="terraformFile">Path to the template file.</param>
    /// <param name="outputFile">Optional output file name.</param>
    /// <param name="appendFile"></param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport("addEnvironmentTerraformTemplate", MethodName = "addTerraformTemplate")]
    public static IResourceBuilder<TerraformTemplateResource> AddTerraformTemplate(this IResourceBuilder<TerraformEnvironmentResource> builder, [ResourceName] string name, string terraformFile, string? outputFile = null, bool appendFile = false)
    {
        var resource = new TerraformTemplateResource(name);

        return builder.ApplicationBuilder.AddResource(resource)
            .WithAnnotation(new TerraformTemplateAnnotation<ValueTemplateResource>
            {
                TemplateFile = terraformFile,
                OutputFile = outputFile,
                AppendFile = appendFile,
                TemplateResource = new ValueTemplateResource() { Name = name, Resource = resource, Outputs = resource.Outputs }
            })
            .WithAnnotation(new DeploymentTargetAnnotation(builder.Resource)
            {
                ComputeEnvironment = builder.Resource
            })
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Adds a specific Terraform template resource to the distributed application builder.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name.</param>
    /// <param name="terraformString">content to the template file.</param>
    /// <param name="outputFile">Optional output file name.</param>
    /// <param name="appendFile"></param>
    /// <returns>The resource builder for chaining.</returns>
    [AspireExport("addEnvironmentTerraformTemplateString", MethodName = "addTerraformTemplateString")]
    public static IResourceBuilder<TerraformTemplateResource> AddTerraformTemplateString(this IResourceBuilder<TerraformEnvironmentResource> builder, [ResourceName] string name, string terraformString, string? outputFile = null, bool appendFile = false)
    {
        var resource = new TerraformTemplateResource(name);

        return builder.ApplicationBuilder.AddResource(resource)
            .WithAnnotation(new TerraformTemplateAnnotation<ValueTemplateResource>
            {
                TemplateString = terraformString,
                OutputFile = outputFile,
                AppendFile = appendFile,
                TemplateResource = new ValueTemplateResource() { Name = name, Resource = resource, Outputs = resource.Outputs },
            })
            .WithAnnotation(new DeploymentTargetAnnotation(builder.Resource)
            {
                ComputeEnvironment = builder.Resource
            })
            .ExcludeFromManifest();
    }
}
