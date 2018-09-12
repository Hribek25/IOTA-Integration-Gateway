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
using Tangle.Net.Entity;

namespace IOTAGears.Controllers
{    
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
        
        private void GetThreadInfo()
        {
            ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableAsyncIOThreads);
            _logger.LogDebug("Available AsyncIOThreads: {availableAsyncIOThreads}, Available Worker Threads: {availableWorkerThreads}", availableWorkerThreads, availableAsyncIOThreads); 
        }
        
        // GET api/tangle/address/transactions
        /// <summary>
        /// All transaction hashes related to the given IOTA address
        /// </summary>
        /// <returns>List of transaction hashes</returns>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("address/{address}/transactions")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.TransactionHashList), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> TransactionsByAddress(string address)
        {
            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest(); //return 400 error
            }

            TransactionHashList res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.Api.GetTransactionsByAddress(address);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionsByAddress));
                return StatusCode(504); // return 504 error       
            }

            return Json(res);                       
            
        }


        // GET api/tangle/bundle/transactions
        /// <summary>
        /// All transaction hashes related to the given bundle
        /// </summary>
        /// <returns>List of transaction hashes</returns>
        /// <response code="400">Incorect format of the bundle hash</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("bundle/{hash}/transactions")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(TransactionHashList), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> TransactionsByBundle(string hash)
        {
            if (!CommonHelpers.IsValidAddress(hash))
            {
                return BadRequest(); //return 400 error
            }

            TransactionHashList res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.Api.GetTransactionsByBundle(hash);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionsByBundle));
                return StatusCode(504); // return 504 error       
            }

            return Json(res);
        }


        // GET api/tangle/address/transactions/details
        /// <summary>
        /// All transactions including all details related to the given IOTA address
        /// </summary>
        /// <remarks>Transactions sorted in a descending order</remarks>
        /// <returns>List of transactions</returns>        
        /// <param name="filter">Filter criteria.<br />Default: ConfirmedOnly</param>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("address/{address}/transactions/details")]
        [CacheTangleResponse(
            LifeSpan = 15,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.OK)]
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        public async Task<IActionResult> TransactionDetailsByAddress(string address, TransactionFilter filter)
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        {
            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest(); //return 400 error
            }
            
            List<TransactionContainer> res;
            try
            {
                res = await _repository.Api.GetDetailedTransactionsByAddress(address);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionDetailsByAddress));
                return StatusCode(504); //returns 504
            }

            List<TransactionContainer> sorted;
            if (filter==TransactionFilter.All)
            { // all transactions
                sorted = (from i in res orderby i.Transaction.Timestamp descending select i).ToList();
            }
            else
            { // only confirmed
                sorted = (from i in res where (bool)i.IsConfirmed orderby i.Transaction.Timestamp descending select i).ToList();
            }
            return Json(sorted);                    
        }


        // GET api/tangle/bundle/transactions/details
        /// <summary>
        /// All transactions including all details related to the given bundle
        /// </summary>
        /// <remarks>Transactions sorted in a descending order</remarks>
        /// <returns>List of transactions</returns>        
        /// <param name="filter">Filter criteria.<br />Default: ConfirmedOnly</param>
        /// <response code="400">Incorect format of the bundle hash</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("bundle/{hash}/transactions/details")]
        [CacheTangleResponse(
            LifeSpan = 15,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.OK)]
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        public async Task<IActionResult> TransactionDetailsByBundle(string hash, TransactionFilter filter)
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        {
            if (!CommonHelpers.IsValidAddress(hash))
            {
                return BadRequest(); //return 400 error
            }

            List<TransactionContainer> res;
            try
            {
                res = await _repository.Api.GetDetailedTransactionsByBundle(hash);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionDetailsByBundle));
                return StatusCode(504); //returns 504
            }

            List<TransactionContainer> sorted;
            if (filter == TransactionFilter.All)
            { // all transactions
                sorted = (from i in res orderby i.Transaction.Timestamp descending select i).ToList();
            }
            else
            { // only confirmed
                sorted = (from i in res where (bool)i.IsConfirmed orderby i.Transaction.Timestamp descending select i).ToList();
            }
            return Json(sorted);
        }


        // GET api/tangle/address/balance
        /// <summary>
        /// Confirmed balance of the given IOTA address based on the latest confirmed milestone
        /// </summary>
        /// <returns></returns>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>  
        [HttpGet("address/{address}/balance")]
        [CacheTangleResponse(
            LifeSpan = 10,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType(typeof(AddressWithBalances), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> BalanceByAddress(string address)
        {
            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest(); //return 400
            }

            AddressWithBalances res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.Api.GetBalanceByAddress(address);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(BalanceByAddress));
                return StatusCode(504);
            }

            return Json(res);
        }


        /// <summary>
        /// Transaction details by transaction hash
        /// </summary>
        /// <returns></returns>
        /// <response code="400">Incorect format of the hash</response>
        /// <response code="504">Result is not available at the moment</response>  
        [HttpGet("transaction/{hash}")]
        [CacheTangleResponse(
            LifeSpan = 10,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> TransactionDetailsByHash(string hash)
        {
            if (!CommonHelpers.IsValidAddress(hash))
            {
                return BadRequest(); //return 400
            }

            List<TransactionContainer> res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.Api.GetDetailedTransactionByHash(hash);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionDetailsByHash));
                return StatusCode(504);
            }

            return Json(res);
        }





        // POST api/tangle/address/sendtx
        /// <summary>
        /// Send non-value transaction to the given IOTA address. Message to be broadcasted should be in the request body
        /// </summary>
        /// <remarks>Transactions may not be sent immediately. All requests are added to a common pipeline which is being processed sequentially.
        /// There is a output parameter <code>NumberOfRequests</code> that indicates how many requests are in the pipeline (inclusive).</remarks>
        /// <returns></returns>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Action could not be performed at the moment</response>  
        /// <response code="429">Too many requests</response>  
        [HttpPost("address/{address}/sendtx")]
        [ProducesResponseType(typeof(PipelineStatus), (int)HttpStatusCode.Accepted)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.TooManyRequests)]
        public async Task<IActionResult> SendTX(string address, [FromBody] string message)
        {
            if (!CommonHelpers.IsValidAddress(address) || !ModelState.IsValid)
            {
                return BadRequest("Bad format of the address or request."); //return 400
            }

            if (message.Length>6500) // message is too long
            {
                return BadRequest("Message is too long."); //return 400
            }
            
            PipelineStatus res;
            try
            {
                res = await _repository.Api.AddTransactionToPipeline(address, message, Request);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(SendTX));
                return StatusCode(504);
            }

            if (res.Status==StatusDetail.TooManyRequests) // too many requests
            {
                return StatusCode(429);
            }

            return Accepted(res);
        }
    }
}
