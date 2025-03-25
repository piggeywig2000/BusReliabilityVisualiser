using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BusReliabilityWeb.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            StadiaAPIKey = configuration.GetValue<string>("StadiaApiKey") ?? "";
        }

        public string StadiaAPIKey { get; set; }

        public void OnGet()
        {

        }
    }
}
