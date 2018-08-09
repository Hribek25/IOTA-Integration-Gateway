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
using System.Net;

namespace IOTA_Gears.Controllers
{
    //[Route("api/[controller]/[action]")]
    [Route("api/[controller]")]
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
        /// <summary>
        /// It provides a basic summary of an IOTA node
        /// </summary>
        /// <returns></returns>
        /// <response code="404">If it fails</response>    
        [HttpGet("node/[action]")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.NodeInfo), (int)HttpStatusCode.OK)]
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

        // GET api/tangle/address/transactions
        //[HttpGet("address/{address}/transactions")]
        //public async Task<IActionResult> Transactions (string address)
        //{

        //}




    }
}
