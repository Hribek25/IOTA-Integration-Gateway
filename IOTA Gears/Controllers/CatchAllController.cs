using Microsoft.AspNetCore.Mvc;

namespace IOTAGears.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class CatchAllController : Controller
    {   
        [Route("{*url}",Order = 999)]        
        public IActionResult CatchAll()
        {
            return View();
        }
    }
}
