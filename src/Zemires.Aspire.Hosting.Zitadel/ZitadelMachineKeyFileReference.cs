using Aspire.Hosting.ApplicationModel;

namespace Zemires.Aspire.Hosting.Zitadel;

/// <summary>
/// Resolves the machine user key file written by Zitadel on first start as base64 encoded JSON.
/// The file is read on every evaluation, as it only exists once the Zitadel container has initialized.
/// </summary>
internal sealed class ZitadelMachineKeyFileReference(ZitadelResource resource) : IValueProvider, IManifestExpressionProvider
{
    public string ValueExpression => $"{{{resource.Name}.machineKey}}";

    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        string? path = resource.MachineUserKeyPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(content);
    }
}
