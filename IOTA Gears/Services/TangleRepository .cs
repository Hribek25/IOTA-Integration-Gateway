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

                // TODO: Consider whether to return everything in cache - there may be more entries because of permanodes
                return res;
            }

            public async Task<List<Transaction>> GetDetailedTransactionsByAddress(string address)
            {
                var callerID = $"FindTransactionsByAddress.Details::{address}";

                TransactionHashList trnList;
                try
                {
                    // Get list of all transactions - local API call
                    trnList = await this.GetTransactionsByAddress(address);
                }
                catch (Exception)
                {
                    throw;
                }

                // getting details

                // Getting from a partial cache to speed up the second call
                var cached = await _DB.GetPartialCacheEntriesAsync(call: callerID);
                var CachedInput = (List<Hash>)cached.Item1 ?? new List<Hash>(); // list of transaction hashes
                var CachedOutput = (IList)cached.Item2; // list of transactions

                // list of transaction hashes that are new (not in the cache)
                var hashesToGet = trnList.Hashes.Except(CachedInput, (a, b) => a.Value==b.Value).ToList();
                
                List < Transaction > resTransactions = new List<Transaction>();
                List<TransactionTrytes> trnTrytes;
                
                if (trnList.Hashes.Count > 0)
                {
                    if (hashesToGet.Count>0) // any new transactions to get info about?
                    {
                        try
                        {
                            trnTrytes = await _repo.GetTrytesAsync(hashesToGet);
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                        resTransactions.AddRange(from i in trnTrytes select Transaction.FromTrytes(i)); // converting to transaction object and adding to collection
                        
                        //Write to partial cache
                        await _DB.AddPartialCacheEntriesAsync(
                            call: callerID,
                            input: hashesToGet.Concat(CachedInput),  // input should include all transaction hashes that are in the cache - old ones + new ones
                            results: resTransactions); // Let's store only newly added items to the cache with a new timestamp
                    }

                    // adding original transations from the cache to the output
                    resTransactions.AddRange(CachedOutput.Cast<Transaction>());
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
