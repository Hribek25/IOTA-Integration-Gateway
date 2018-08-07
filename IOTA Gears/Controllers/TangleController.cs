using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using Tangle.Net.Repository;
using IOTA_Gears.Services;
using Microsoft.Extensions.Logging;
using IOTA_Gears.ActionFilters;

namespace IOTA_Gears.Controllers
{
    [Route("api/[controller]/[action]")]
    public class TangleController : Controller
    {
        private readonly TangleRepository _repository;
        private readonly ILogger<TangleController> _logger;

        //CTOR
        public TangleController(ITangleRepository repo, ILogger<TangleController> logger) // dependency injection
        {
            _repository = (TangleRepository)repo;
            _logger = logger;
        }
        //CTOR
                
        
        // GET api/tangle/getnodeinfo
        [HttpGet]
        [CacheTangleResponse(LifeSpan = 300)]
        [Produces("application/javascript")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetNodeInfo()
        {
            Tangle.Net.Repository.DataTransfer.NodeInfo res;
            try
            {
                res = await _repository.Api.GetNodeInfoAsync();                
            }
            catch (Exception)
            {
                return new NotFoundResult() ;
            }            
            return Json(res); // Format the output
        }                 
    }
}
