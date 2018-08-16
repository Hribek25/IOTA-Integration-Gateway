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
        /// Basic summary of an IOTA node and its status. It calls core IOTA API call: getNodeInfo()
        /// </summary>
        /// <returns></returns>
        /// <response code="404">Failure</response>    
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
        /// <summary>
        /// All transaction hashes related to the given IOTA address. It calls core IOTA API call: findTransactions()
        /// </summary>
        /// <returns></returns>
        /// <response code="404">Failure</response>    
        [HttpGet("address/{address:regex(^(([[A-Z9]]{{90}})|([[A-Z9]]{{81}}))$)}/transactions")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.TransactionHashList), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> Transactions(string address)
        {
            Tangle.Net.Repository.DataTransfer.TransactionHashList res;
                        
            try
            {
                // get a list of transactions to the given address
                res = await _repository.Api.GetTransactionsByAddress(address);
            }
            catch (Exception)
            {
                return new NotFoundResult();
            }

            return Json(res);

            //IEnumerable<Tangle.Net.Entity.Bundle> bundles = null;
            //if (res.Hashes.Count > 0)
            //{
            //    bundles = await _repository.Api._repo.GetBundlesAsync(
            //        transactionHashes: (from i in res.Hashes
            //        select i).ToList(),
            //        includeInclusionStates: false);
            //}
            
        }




        // GET api/tangle/address/transactions/details
        /// <summary>
        /// All transactions including all details related to the given IOTA address. It calls core IOTA API calls: findTransactions() + getTrytes()
        /// Transactions are sorted in a descending order by default
        /// </summary>
        /// <returns></returns>
        /// <response code="404">Failure</response>    
        [HttpGet("address/{address:regex(^(([[A-Z9]]{{90}})|([[A-Z9]]{{81}}))$)}/transactions/details")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(List<Tangle.Net.Entity.Transaction>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> TransactionsDetails(string address)
        {
            List<Tangle.Net.Entity.Transaction> res;            
            try
            {
                res = await _repository.Api.GetDetailedTransactionsByAddress(address);
            }
            catch (Exception)
            {
                return new NotFoundResult();
            }

            var sorted = (from i in res orderby i.Timestamp descending select i).ToList();
            return Json(sorted);                    
        }





        // GET api/tangle/address/balance
        /// <summary>
        /// Confirmed balance of the given IOTA address based on the latest confirmed milestone. It calls core IOTA API call: getBalances()
        /// </summary>
        /// <returns></returns>
        /// <response code="404">Failure</response>    
        [HttpGet("address/{address:regex(^(([[A-Z9]]{{90}})|([[A-Z9]]{{81}}))$)}/balance")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.AddressWithBalances), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> Balance(string address)
        {
            Tangle.Net.Repository.DataTransfer.AddressWithBalances res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.Api.GetBalanceByAddress(address);
            }
            catch (Exception)
            {
                return new NotFoundResult();
            }

            return Json(res);
        }
    }
}
