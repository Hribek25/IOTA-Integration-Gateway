using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Tangle.Net.Repository.DataTransfer;
using IOTAGears.Services;
using Microsoft.Extensions.Logging;
using IOTAGears.ActionFilters;
using System.Net;
using IOTAGears.EntityModels;
using System.Threading;

namespace IOTAGears.Controllers
{    
    [Route("api/[controller]")]
    public class NodeController : Controller
    {
        private readonly TangleRepository _repository;
        private readonly ILogger<TangleController> _logger;

        //CTOR
        public NodeController(ITangleRepository repo, ILogger<TangleController> logger) // dependency injection
        {
            _repository = (TangleRepository)repo;
            _logger = logger;
        }
        //CTOR
        
        // GET api/node/getnodeinfo
        /// <summary>
        /// Basic summary of an IOTA node and its status
        /// </summary>
        /// <returns></returns>
        /// <response code="504">Result is not available at the moment</response>    
        [HttpGet("[action]")]
        [CacheTangleResponse(
            LifeSpan = 20,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType(typeof(NodeInfo), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetNodeInfo()
        {
            NodeInfo res;
            try
            {
                res = await _repository.Api.GetNodeInfoAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(GetNodeInfo));
                return StatusCode(504); // return 404 error
            }            
            return Json(res); // Format the output
        }
    }
}
