﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Tangle.Net.Repository.DataTransfer;
using IOTA_Gears.Services;
using Microsoft.Extensions.Logging;
using IOTA_Gears.ActionFilters;
using System.Net;
using IOTA_Gears.EntityModels;

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
        /// <response code="504">Result is not available at the moment</response>    
        [HttpGet("node/[action]")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.NodeInfo), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetNodeInfo()
        {
            NodeInfo res;
            try
            {
                res = await _repository.Api.GetNodeInfoAsync();
            }

            catch (Exception e)
            {
                _logger.LogError(e, "Error occured");
                return StatusCode(504); // return 404 error
            }            
            return Json(res); // Format the output
        }



        // GET api/tangle/address/transactions
        /// <summary>
        /// All transaction hashes related to the given IOTA address. It calls core IOTA API call: findTransactions()
        /// </summary>
        /// <returns></returns>
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
        public async Task<IActionResult> Transactions(string address)
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
                _logger.LogError(e, "Error occured in Transactions controller");
                return StatusCode(504); // return 404 error                
            }

            return Json(res);                       
            
        }




        // GET api/tangle/address/transactions/details
        /// <summary>
        /// All transactions including all details related to the given IOTA address. It calls core IOTA API calls: findTransactions() + getTrytes() + getLatestInclusionStates()
        /// </summary>
        /// <returns>Transactions sorted in a descending order.</returns>
        /// <param name="filter">ConfirmedOnly / All</param>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="504">Result is not available at the moment</response>
        [HttpGet("address/{address}/transactions/details")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(List<EntityModels.TransactionContainer>), (int)HttpStatusCode.OK)]
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        public async Task<IActionResult> TransactionsDetails(string address, TransactionFilter filter = TransactionFilter.ConfirmedOnly)
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
        {
            //_logger.LogDebug("{filter}", filter);

            if (!CommonHelpers.IsValidAddress(address))
            {
                return BadRequest(); //return 400 error
            }
            
            List<EntityModels.TransactionContainer> res;
            try
            {
                res = await _repository.Api.GetDetailedTransactionsByAddress(address);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured in TransactionsDetails controller");
                return StatusCode(504); //returns 404
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
        
        
        // GET api/tangle/address/balance
        /// <summary>
        /// Confirmed balance of the given IOTA address based on the latest confirmed milestone. It calls core IOTA API call: getBalances()
        /// </summary>
        /// <returns></returns>
        /// <response code="400">Incorect format of the address</response>
        /// <response code="404">Result is not available at the moment</response>    
        [HttpGet("address/{address}/balance")]
        [CacheTangleResponse(
            LifeSpan = 300,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType(typeof(Tangle.Net.Repository.DataTransfer.AddressWithBalances), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.GatewayTimeout)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> Balance(string address)
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
                _logger.LogError(e, "Error occured in Balance controller");
                return StatusCode(504);
            }

            return Json(res);
        }
    }
}
