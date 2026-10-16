namespace Aspire.Hosting;

/// <summary>
/// Metric categories exposed by n8n. All are enabled by default.
/// </summary>
[AspireExport(ExposeProperties = true)]
public class N8nMetricOptions
{
    /// <summary>Include webhook metrics (<c>N8N_METRICS_INCLUDE_WEBHOOK_METRICS</c>).</summary>
    public bool IncludeWebhookMetrics { get; set; } = true;

    /// <summary>Include workflow information (<c>N8N_METRICS_INCLUDE_WORKFLOW_INFO</c>).</summary>
    public bool IncludeWorkflowInfo { get; set; } = true;

    /// <summary>Include form metrics (<c>N8N_METRICS_INCLUDE_FORM_METRICS</c>).</summary>
    public bool IncludeFormMetrics { get; set; } = true;

    /// <summary>Include the workflow id label (<c>N8N_METRICS_INCLUDE_WORKFLOW_ID_LABEL</c>).</summary>
    public bool IncludeWorkflowIdLabel { get; set; } = true;

    /// <summary>Include the node type label (<c>N8N_METRICS_INCLUDE_NODE_TYPE_LABEL</c>).</summary>
    public bool IncludeNodeTypeLabel { get; set; } = true;

    /// <summary>Include the credential type label (<c>N8N_METRICS_INCLUDE_CREDENTIAL_TYPE_LABEL</c>).</summary>
    public bool IncludeCredentialTypeLabel { get; set; } = true;

    /// <summary>Include API endpoint metrics (<c>N8N_METRICS_INCLUDE_API_ENDPOINTS</c>).</summary>
    public bool IncludeApiEndpoints { get; set; } = true;

    /// <summary>Include the API path label (<c>N8N_METRICS_INCLUDE_API_PATH_LABEL</c>).</summary>
    public bool IncludeApiPathLabel { get; set; } = true;

    /// <summary>Include queue metrics (<c>N8N_METRICS_INCLUDE_QUEUE_METRICS</c>).</summary>
    public bool IncludeQueueMetrics { get; set; } = true;
}
