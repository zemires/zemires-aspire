using Aspire.Hosting;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

#pragma warning disable ASPIREPIPELINES002
#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

namespace Zemires.Aspire.Hosting.Terraform;

/// <summary>
/// Executes Terraform commands for initialization, planning, and applying infrastructure changes,
/// using deployment state and configuration to populate environment variables.
/// </summary>
public class TerraformExecutor
{
    private string _tfExecutable;
    private readonly ILogger<TerraformExecutor> _logger;
    private readonly IOptions<PipelineOptions> _publishingOptions;
    private readonly IDeploymentStateManager _deploymentStateManager;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerraformExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for this executor.</param>
    /// <param name="publishingOptions">The pipeline publishing options.</param>
    /// <param name="deploymentStateManager">Manager for deployment state sections.</param>
    /// <param name="configuration">Application configuration; the constructor reads the "Terraform:Executor" section.</param>
    public TerraformExecutor(ILogger<TerraformExecutor> logger,
        IOptions<PipelineOptions> publishingOptions,
        IDeploymentStateManager deploymentStateManager,
        IConfiguration configuration)
    {
        _logger = logger;
        _publishingOptions = publishingOptions;
        _deploymentStateManager = deploymentStateManager;
        _configuration = configuration.GetSection("Terraform:Executor");

        _tfExecutable = _configuration.GetValue<string>("ExecutablePath", "tofu");
    }

    /// <summary>
    /// Initializes Terraform and applies the current plan without prompting for input.
    /// Populates Terraform environment variables from deployment state and configuration.
    /// </summary>
    /// <param name="cancellationToken">Token to observe while waiting for the operation to complete.</param>
    /// <param name="additionalArgs"></param>
    /// <param name="terraformVariables"></param>
    /// <returns>A task that represents the asynchronous apply operation.</returns>
    /// <remarks>Logs output and errors; throws if the Terraform command exits with a non-zero code.</remarks>
    public async Task Apply(CancellationToken cancellationToken, string? additionalArgs = null, Dictionary<string, string>? terraformVariables = null)
    {
        var tfEnv = await BuildTerraformEnvironmentVariables(cancellationToken);

        if (terraformVariables != null)
        {
            foreach (var kvp in terraformVariables)
            {
                tfEnv["TF_VAR_" + kvp.Key] = kvp.Value;
            }
        }

        try
        {
            await TerraformCommand("init -reconfigure", cancellationToken);
            await TerraformCommand($"apply -input=false -auto-approve {additionalArgs}", cancellationToken, tfEnv);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error Apply");
        }
    }

    /// <summary>
    /// Initializes Terraform and generates a plan without prompting for input.
    /// Populates Terraform environment variables from deployment state and configuration.
    /// </summary>
    /// <param name="cancellationToken">Token to observe while waiting for the operation to complete.</param>
    /// <param name="additionalArgs"></param>
    /// <param name="terraformVariables"></param>
    /// <returns>A task that represents the asynchronous plan operation.</returns>
    /// <remarks>Logs output and errors; throws if the Terraform command exits with a non-zero code.</remarks>
    public async Task Plan(CancellationToken cancellationToken, string? additionalArgs = null, Dictionary<string, string>? terraformVariables = null)
    {
        var tfEnv = await BuildTerraformEnvironmentVariables(cancellationToken);

        if (terraformVariables != null)
        {
            foreach (var kvp in terraformVariables)
            {
                tfEnv["TF_VAR_" + kvp.Key] = kvp.Value;
            }
        }

        try
        {
            await TerraformCommand("init -reconfigure", cancellationToken);
            await TerraformCommand($"plan -input=false {additionalArgs}", cancellationToken, tfEnv);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error Plan");
        }
    }

    /// <summary>
    /// Destroys the Terraform-managed infrastructure without prompting for input.
    /// </summary>
    /// <param name="cancellationToken">Token to observe while waiting for the operation to complete.</param>
    /// <param name="additionalArgs"></param>
    /// <param name="terraformVariables"></param>
    /// <returns>A task that represents the asynchronous plan operation.</returns>
    /// <remarks>Logs output and errors; throws if the Terraform command exits with a non-zero code.</remarks>
    public async Task Destroy(CancellationToken cancellationToken, string? additionalArgs = null, Dictionary<string, string>? terraformVariables = null)
    {
        var tfEnv = await BuildTerraformEnvironmentVariables(cancellationToken);

        if (terraformVariables != null)
        {
            foreach (var kvp in terraformVariables)
            {
                tfEnv["TF_VAR_" + kvp.Key] = kvp.Value;
            }
        }

        try
        {
            await TerraformCommand("init -reconfigure", cancellationToken);
            await TerraformCommand($"destroy -input=false -auto-approve {additionalArgs}", cancellationToken, tfEnv);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error Destroy");
        }
    }

    /// <summary>
    /// Reads a single Terraform output value (<c>terraform output -raw</c>).
    /// </summary>
    /// <param name="name">The output name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw output value.</returns>
    public async Task<string> GetOutput(string name, CancellationToken cancellationToken)
    {
        return await ProcessExecutorHelper.ExecuteProcessOutput(_tfExecutable, $"output -raw {name}", workingDirectory: WorkingDirectory, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads all Terraform output values (<c>terraform output -json</c>).
    /// </summary>
    /// <param name="includeSensitive">Whether to include outputs marked as sensitive.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The output values keyed by output name.</returns>
    public async Task<Dictionary<string, string>> GetOutputs(bool includeSensitive, CancellationToken cancellationToken)
    {
        var output = await ProcessExecutorHelper.ExecuteProcessOutput(_tfExecutable, $"output -json", workingDirectory: WorkingDirectory, cancellationToken: cancellationToken);
        var jsonDoc = JsonDocument.Parse(output);

        var dic = new Dictionary<string, string>();

        foreach (var element in jsonDoc.RootElement.EnumerateObject())
        {
            var name = element.Name;
            var value = element.Value.GetProperty("value");
            var isSensitive = element.Value.GetProperty("sensitive").GetBoolean();

            if (isSensitive && !includeSensitive)
                continue;

            dic.Add(name, value.ToString());
        }

        return dic;
    }

    private async Task TerraformCommand(string arguments, CancellationToken cancellationToken = default, Dictionary<string, string?>? environmentVariables = null)
    {
        _logger.LogInformation("Call " + arguments);

        var exitCode = await ProcessExecutorHelper.ExecuteProcess(_tfExecutable, arguments, _logger, cancellationToken, WorkingDirectory, environmentVariables);

        if (exitCode != 0)
        {
            _logger.LogError("Terraform Command Failed with exit code {ExitCode}", exitCode);
            throw new InvalidOperationException($"Terraform command failed: {exitCode}");
        }
    }

    private async Task<Dictionary<string, string?>> BuildTerraformEnvironmentVariables(CancellationToken cancellationToken = default)
    {
        var tfEnv = new Dictionary<string, string?>();

        var parametersSection = await _deploymentStateManager.AcquireSectionAsync("Parameters", cancellationToken);
        foreach (var paramKeyValue in parametersSection.Data)
        {
            tfEnv.Add("TF_VAR_" + paramKeyValue.Key, paramKeyValue.Value?.ToString());
        }

        var terraformSection = await _deploymentStateManager.AcquireSectionAsync("Terraform", cancellationToken);
        var configVariables = _configuration.GetSection("Terraform").GetSection("Variables");
        foreach (var configVariable in configVariables.GetChildren())
        {
            if (!terraformSection.Data.ContainsKey(configVariable.Key))
            {
                terraformSection.Data.Add(configVariable.Key, configVariable.Value);
            }
        }

        await _deploymentStateManager.SaveSectionAsync(terraformSection, cancellationToken);

        foreach (var paramKeyValue in terraformSection.Data)
        {
            tfEnv.Add("TF_VAR_" + paramKeyValue.Key, paramKeyValue.Value?.ToString());
        }

        return tfEnv;
    }

    private string WorkingDirectory => Path.GetFullPath(_publishingOptions.Value.OutputPath ?? "./.terraform");
}
