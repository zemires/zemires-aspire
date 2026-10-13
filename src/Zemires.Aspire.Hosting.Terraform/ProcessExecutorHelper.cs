using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
#pragma warning disable ASPIREPIPELINES002
#pragma warning disable ASPIREPIPELINES001
#pragma warning disable IDE0130

namespace Aspire.Hosting;

internal static class ProcessExecutorHelper
{
    internal static async Task<int> ExecuteProcess(string fileName, string arguments, ILogger? logger = null, CancellationToken cancellationToken = default, string? workingDirectory = null, Dictionary<string, string?>? environmentVariables = null)
    {
        var process = new Process();
        process.StartInfo = new ProcessStartInfo()
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        if (environmentVariables != null)
        {
            foreach (var environmentVariable in environmentVariables)
            {
                process.StartInfo.Environment.Add(environmentVariable);
            }
        }

        if (logger != null)
        {
            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null) logger.LogInformation(args.Data);
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null) logger.LogError(args.Data);
            };
        }

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return process.ExitCode;
    }

    internal static async Task<string> ExecuteProcessOutput(string fileName, string arguments, CancellationToken cancellationToken = default, string? workingDirectory = null, Dictionary<string, string?>? environmentVariables = null)
    {
        var process = new Process();
        process.StartInfo = new ProcessStartInfo()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        if (environmentVariables != null)
        {
            foreach (var environmentVariable in environmentVariables)
            {
                process.StartInfo.Environment.Add(environmentVariable);
            }
        }

        var output = new StringBuilder();

        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null) output.Append(args.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Execution failed: {process.ExitCode}");
        }

        return output.ToString();
    }
}
