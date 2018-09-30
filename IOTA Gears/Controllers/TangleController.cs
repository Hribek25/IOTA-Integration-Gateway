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
        private readonly Logger<TangleController> _logger;

        //CTOR
        public TangleController(ITangleRepository repo, ILogger<TangleController> logger) // dependency injection
        {
            _repository = (TangleRepository)repo;
            _logger = (Logger<TangleController>)logger;
        }
        //CTOR
        
        private void GetThreadInfo()
        {
            ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableAsyncIOThreads);
            _logger.LogDebug("Available AsyncIOThreads: {availableAsyncIOThreads}, Available Worker Threads: {availableWorkerThreads}", availableWorkerThreads, availableAsyncIOThreads); 
        }
        
        // GET api/tangle/address/transactions
        /// <summary>
        /// Transaction hashes by IOTA address
        /// </summary>
        /// <returns>List of transaction hashes</returns>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("address/{address}/transactions")]
        [CacheTangleResponse(
            LifeSpan = 45,
            StatusCodes = new int[] { (int)HttpStatusCode.OK })
            ]
        [Produces("application/json")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        // [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.TransactionHashList), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> TransactionsByAddress(string address)
        {
            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest("Incorect format of the address"); //return 400 error                
            }

            HashSet<string> res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.GetTransactionsByAddress(address);
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
        /// Transaction hashes by IOTA bundle
        /// </summary>
        /// <returns>List of transaction hashes</returns>
        /// <response code="400">Incorect format of the bundle hash</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("bundle/{hash}/transactions")]
        [CacheTangleResponse(
            LifeSpan = 45,
            StatusCodes = new int[] { (int)HttpStatusCode.OK })
            ]
        [Produces("application/json")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(TransactionHashList), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> TransactionsByBundle(string hash)
        {
            if (!CommonHelpers.IsValidHash(hash))
            {
                return BadRequest("Incorect format of the bundle hash"); //return 400 error
            }

            HashSet<string> res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.GetTransactionsByBundle(hash);
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
        /// Transactions with all details by IOTA address
        /// </summary>
        /// <remarks>Transactions are sorted by timestamp in descending order</remarks>
        /// <returns>List of transactions</returns>        
        /// <param name="filter">Filter criteria.<br />Default: ConfirmedOnly</param>
        /// <response code="400">Incorrect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>
        /// <response code="206">Number of transactions is limited to 500. If number of TXs is higher then only 500 TXs are returned</response>
        [HttpGet("address/{address}/transactions/details")]
        [CacheTangleResponse(
            LifeSpan = 45,
            StatusCodes = new int[] { (int)HttpStatusCode.OK, (int)HttpStatusCode.PartialContent })
            ]
        [Produces("application/json")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.PartialContent)]
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        public async Task<IActionResult> TransactionDetailsByAddress(string address, TransactionFilter filter)
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        {
            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest("Incorect format of the address"); //return 400 error
            }

            TxHashSetCollection res;
            try
            {
                res = await _repository.GetDetailedTransactionsByAddress(address);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionDetailsByAddress));
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

            if (res.CompleteSet)
            {
                return Json(sorted);
            }
            else
            {
                return StatusCode((int)HttpStatusCode.PartialContent, sorted);
            }            
        }


        /// <summary>
        /// Transactions with all details by IOTA bundle
        /// </summary>
        /// <remarks>Transactions are sorted by timestamp in descending order</remarks>
        /// <returns>List of transactions</returns>        
        /// <param name="filter">Filter criteria.<br />Default: ConfirmedOnly</param>
        /// <response code="400">Incorect format of the bundle hash</response>
        /// <response code="504">Result is not available at the moment</response>        
        [HttpGet("bundle/{hash}/transactions/details")]
        [CacheTangleResponse(
            LifeSpan = 45,
            StatusCodes = new int[] { (int)HttpStatusCode.OK })
            ]
        [Produces("application/json")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.OK)]        
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        public async Task<IActionResult> TransactionDetailsByBundle(string hash, TransactionFilter filter)
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        {
            if (!CommonHelpers.IsValidHash(hash))
            {
                return BadRequest("Incorect format of the bundle hash"); //return 400 error
            }

            HashSet<TransactionContainer> res;
            try
            {
                res = await _repository.GetDetailedTransactionsByBundle(hash);
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
        /// Confirmed balance by IOTA address
        /// </summary>
        /// <remarks>
        /// It is based on the latest confirmed milestone
        ///  </remarks>
        /// <returns>IOTA balance</returns>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>  
        [HttpGet("address/{address}/balance")]
        [CacheTangleResponse(
            LifeSpan = 45,
            StatusCodes = new int[] { (int)HttpStatusCode.OK })
            ]
        [Produces("application/json")]
        [ProducesResponseType(typeof(AddressWithBalances), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> BalanceByAddress(string address)
        {
            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest("Incorect format of the address"); //return 400
            }

            AddressWithBalances res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.GetBalanceByAddress(address);
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
        /// <returns>All details by the given transaction</returns>
        /// <response code="400">Incorect format of the hash</response>
        /// <response code="504">Result is not available at the moment</response>  
        [HttpGet("transaction/{hash}")]
        [CacheTangleResponse(
            LifeSpan = 45,
            StatusCodes = new int[] { (int)HttpStatusCode.OK })
            ]
        [Produces("application/json")]
        [ProducesResponseType(typeof(List<TransactionContainer>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> TransactionDetailsByHash(string hash)
        {
            if (!CommonHelpers.IsValidHash(hash))
            {
                return BadRequest("Incorect format of the hash"); //return 400
            }

            HashSet<TransactionContainer> res;
            try
            {
                // get a list of transactions to the given address
                res = await _repository.GetDetailedTransactionByHash(hash);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in " + nameof(TransactionDetailsByHash));
                return StatusCode(504);
            }

            return Json(res); // it will be only one transation and so no ordering
        }





        // POST api/tangle/address/sendtx
        /// <summary>
        /// Send non-value transaction to the given IOTA address
        /// </summary>
        /// <remarks>Message to be broadcasted should be in the request body.
        /// Message is split into several transactions if needed. Transactions may not be sent immediately. All requests are added to a pipeline which is being processed sequentially
        /// </remarks>
        /// <returns>Confirmation that your task was added to the pipeline including an unique id that identifies your particular request.
        /// There is a output parameter <code>NumberOfRequests</code> that indicates how many requests are in the pipeline (inclusive) before your request.</returns>
        /// <response code="202">Your message has been accepted by the gateway and will be broadcasted as soon as possible</response>
        /// <response code="400">Incorect format of the address / parameters / message is too long</response>
        /// <response code="504">Action could not be performed at the moment</response>  
        /// <response code="429">Too many requests. There is a hard limit on requests/second</response>  
        [HttpPost("address/{address}/sendtx")]
        [ProducesResponseType(typeof(PipelineStatus), (int)HttpStatusCode.Accepted)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.TooManyRequests)]
        public async Task<IActionResult> SendTX(string address, [FromBody] string message)
        {
            if (!CommonHelpers.IsValidAddress(address) || !ModelState.IsValid)
            {
                return BadRequest("Bad format of the address or request parameters"); //return 400
            }

            if (message.Length>6500) // message is too long
            {
                return BadRequest("Message is too long"); //return 400
            }
            
            PipelineStatus res;
            try
            {
                res = await _repository.AddTransactionToPipeline(address, message, Request);
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

            return Accepted(res); // 202 response
        }
    }
}
