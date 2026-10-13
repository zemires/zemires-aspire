using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Qute.Directus;
using Qute.Directus.Models;

namespace DirectusApp.Web.Pages;

public class IndexModel(DirectusClient Directus, ILogger<IndexModel> Logger) : PageModel
{

    public async Task OnGet()
    {
        var items = await Directus.Items.GetManyAsync("pages", new QueryParameters().Fields("*", "blocks.*", "blocks.item.*.*.*.*"));
        foreach (var item in items)
        {
            Logger.LogInformation("Item {item}", item);
        }
    }
}


//public class PageModel()
//{
//    public Guid Id { get; set; }
//    public string Title { get; set; }

//    public string Permalink { get; set; }

//    public string Status { get; set; }

//    public IList<BlockModel> Blocks { get; set; }

//}

//public class BlockModel()
//{

//}