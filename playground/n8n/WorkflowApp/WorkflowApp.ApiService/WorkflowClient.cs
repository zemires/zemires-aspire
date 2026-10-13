public class WorkflowClient(HttpClient httpClient, ILogger<WorkflowClient> logger)
{
    public async Task TestWorkflowWebhook()
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/webhook/workflow1-trigger", "hi");
            var info = await response.Content.ReadFromJsonAsync<WebhookInfo>();

            Thread.Sleep(500);

            logger.LogInformation("Resume {url}", info.ResumeUrl);

            var resumeClient = new HttpClient();
            await resumeClient.GetAsync(info.ResumeUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error Start");
        }
    }
}

public record WebhookInfo(string ResumeUrl);
