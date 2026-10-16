using Zitadel.Api;
using Zitadel.Credentials;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Access to the machine user of a <see cref="ZitadelResource"/>, e.g. in initialization code.
/// </summary>
public static class ZitadelResourceExtensions
{
    /// <summary>
    /// Loads the machine user service account from <see cref="ZitadelResource.MachineUserKeyParameter"/> or the key file.
    /// </summary>
    /// <param name="zitadel">The Zitadel resource.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The service account.</returns>
    /// <exception cref="InvalidOperationException">No machine user key is available.</exception>
    [AspireExportIgnore(Reason = "Returns a Zitadel ServiceAccount, which is not ATS-compatible.")]
    public static async Task<ServiceAccount> GetMachineServiceAccount(this ZitadelResource zitadel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zitadel);

        if (zitadel.MachineUserKeyParameter is not null)
        {
            var machineKey = await zitadel.MachineUserKeyParameter.GetValueAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The machine user key of '{zitadel.Name}' has no value.");

            using var machineKeyBuffer = new MemoryStream(Convert.FromBase64String(machineKey));
            return await ServiceAccount.LoadFromJsonStreamAsync(machineKeyBuffer).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(zitadel.MachineUserKeyPath) || !File.Exists(zitadel.MachineUserKeyPath))
        {
            throw new InvalidOperationException($"The machine user key file of '{zitadel.Name}' does not exist, configure it with WithMachineUser.");
        }

        return await ServiceAccount.LoadFromJsonFileAsync(zitadel.MachineUserKeyPath).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates API client options authenticated as the machine user.
    /// </summary>
    /// <param name="zitadel">The Zitadel resource.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The API client options.</returns>
    /// <exception cref="InvalidOperationException">No machine user key is available.</exception>
    [AspireExportIgnore(Reason = "Returns Zitadel API client options, which are not ATS-compatible.")]
    public static async Task<Clients.Options> CreateApiClientOptions(this ZitadelResource zitadel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zitadel);

        ServiceAccount serviceAccount = await zitadel.GetMachineServiceAccount(cancellationToken).ConfigureAwait(false);

        ITokenProvider tokenProvider = ITokenProvider.ServiceAccount(
            zitadel.PrimaryEndpoint.Url,
            serviceAccount,
            new ServiceAccount.AuthOptions { ApiAccess = true });

        return new Clients.Options(zitadel.PrimaryEndpoint.Url, tokenProvider);
    }
}
