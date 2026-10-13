using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Zemires.N8n.Api;

public partial class N8nClient
{
	public N8nClient(string baseUrl, string? apiKey) : this(new HttpClientRequestAdapter(
        new N8nAuthenticationProvider(apiKey)))
	{
		RequestAdapter.BaseUrl = $"{baseUrl}{RequestAdapter.BaseUrl}";
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "{+baseurl}/healthz",
            };
            var response = await RequestAdapter.SendPrimitiveAsync<string>(requestInfo, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response == "OK";
        }
        catch
        {
            return false;
        }
    }
}
