using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tangle.Net.Repository;
using Tangle.Net.Repository.DataTransfer;
using Tangle.Net.Entity;


namespace IOTA_Gears.Services
{
    public interface ITangleRepository
    {
        
    }

    public class TangleRepository : ITangleRepository
    {
        public ApiTasks Api { get; }
        private NodeManager NodeManager { get;  }
        private DBManager DB { get; }
        private ILogger<TangleRepository> Logger { get;  } 
        
        public TangleRepository(NodeManager nodemanager, ILogger<TangleRepository> logger, DBManager dbmanager) {
            NodeManager = nodemanager;
            Logger = logger;
            DB = dbmanager;

            var node = NodeManager.SelectNode(); // TODO: add some smart logic for node selection - round robin?
            Api = new ApiTasks(
                InitRestClient(node),
                dbmanager
                );
        }
        
        private RestIotaRepository InitRestClient(string node)
        {
            var res = new RestIotaRepository(new RestClient(node));
            Logger.LogInformation("TangleRepository initiated... selected node: {node}", node);
            return res;
        }
        
        public class ApiTasks
        {
            public RestIotaRepository _repo { get; }
            public DBManager _DB { get; }

            public ApiTasks(RestIotaRepository repo, DBManager db)
            {
                _repo = repo;
                _DB = db;
            }

            public async Task<NodeInfo> GetNodeInfoAsync()
            {
                NodeInfo res;
                try
                {
                    res = await _repo.GetNodeInfoAsync();
                }
                catch (Exception)
                {
                    throw;
                }
                return res;
            }

            public async Task<TransactionHashList> GetTransactionsByAddress(string address)
            {
                // get a list of transactions to the given address
                var callerID = $"FindTransactionsByAddress::{address}";

                // Get from a partial cache
                var cached = await _DB.GetPartialCacheEntriesAsync(call: callerID);
                var CachedInput = cached.Item1;
                var CachedOutput = (IList)cached.Item2;

                // Get info from node
                TransactionHashList res;
                try
                {
                    res = await _repo.FindTransactionsByAddressesAsync( new List<Tangle.Net.Entity.Address>() { new Tangle.Net.Entity.Address(address) } );
                }
                catch (Exception)
                {
                    throw;
                }
                
                //Write to partial cache
                await _DB.AddPartialCacheEntriesAsync(
                    call: callerID,
                    input: address,
                    results: from i in res.Hashes where !CachedOutput.Contains(i.Value) select i.Value); // Let's store only newly added items to the cache with a new timestamp and so I can check what have been new ones

                return res;
            }

            public async Task<List<Transaction>> GetDetailedTransactionsByAddress(string address)
            {
                // get a list of transactions to the given address
                var callerID = $"FindTransactionsByAddress.Details::{address}";

                // Get list of transactions
                TransactionHashList trnList;
                try
                {
                    trnList = await this.GetTransactionsByAddress(address);
                }
                catch (Exception)
                {
                    throw;
                }

                // getting details

                List<Transaction> resTransactions = new List<Transaction>();
                List<TransactionTrytes> trnTrytes;
                
                if (trnList.Hashes.Count > 0)
                {
                    try
                    {
                        trnTrytes = await _repo.GetTrytesAsync(trnList.Hashes);
                    }
                    catch (Exception)
                    {

                        throw;
                    }

                    resTransactions.AddRange(from i in trnTrytes select Transaction.FromTrytes(i)); // converting to transaction object and adding to collection
                }               

                return resTransactions;
            }


            public async Task<AddressWithBalances> GetBalanceByAddress(string address)
            {
                // get a list of transactions to the given address
                var callerID = $"GetBalanceByAddress::{address}";

                // Get from a partial cache
                var cached = await _DB.GetPartialCacheEntryAsync(call: callerID);
                var CachedInput = cached.Item1;
                var CachedOutput = (AddressWithBalances)cached.Item2;

                // Get info from node
                AddressWithBalances res;
                try
                {
                    res = await _repo.GetBalancesAsync(new List<Tangle.Net.Entity.Address>() { new Tangle.Net.Entity.Address(address) });
                }
                catch (Exception)
                {
                    throw;
                }

                if (CachedOutput==null || CachedOutput.Addresses[0].Balance!=res.Addresses[0].Balance) // balance has been changed - adding new one with a new timestamp
                {
                    //Write to partial cache
                    await _DB.AddPartialCacheEntryAsync(
                        call: callerID,
                        input: address,
                        result: res); // Let's add actual balance
                }                               

                return res;
            }
        }       

    }

}
