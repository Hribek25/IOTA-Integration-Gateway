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
                this
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
            private DBManager _DB { get; }
            private ILogger<TangleRepository> _Logger { get; }

            public ApiTasks(RestIotaRepository repo, TangleRepository parent)
            {
                _repo = repo;
                _DB = parent.DB;
                _Logger = parent.Logger;
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
                var CachedInput = (string)cached.Item1; // it may be null
                var CachedOutput = cached.Item2==null ? new List<Hash>() : cached.Item2.Cast<Hash>().ToList<Hash>();

                // Get info from node
                TransactionHashList res;
                try
                {
                    res = await _repo.FindTransactionsByAddressesAsync( new List<Address>() { new Address(address) } );
                }
                catch (Exception)
                {
                    throw;
                }
                var OnlyNewHashes = res.Hashes.Except(CachedOutput,(a,b) => a.Value==b.Value).ToList();
                if (OnlyNewHashes.Count > 0)
                {
                    //Write to partial cache
                    await _DB.AddPartialCacheEntriesAsync(
                        call: callerID,
                        input: address,
                        results: OnlyNewHashes); // Let's store only newly added items to the cache with a new timestamp and so I can check what have been new ones
                }

                _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transaction hashes loaded from cache. {OnlyNewHashes.Count} hashes were new ones.", callerID, CachedOutput.Count, OnlyNewHashes.Count);

                res.Hashes = CachedOutput.Concat(OnlyNewHashes).ToList() ; //returning all from cache + new one from API call
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
                var CachedOutput = cached.Item2 == null ? new List<Transaction>() : cached.Item2.Cast<Transaction>().ToList<Transaction>();
                
                //todo: check number of input items vs ouptu items


                // list of transaction hashes that are new (not in the cache)
                var OnlyNewHashes = trnList.Hashes.Except(CachedInput, (a, b) => a.Value==b.Value).ToList();
                
                List <Transaction> resTransactions = new List<Transaction>();
                if (trnList.Hashes.Count > 0)
                {
                    if (OnlyNewHashes.Count>0) // any new transactions to get info about?
                    {
                        List<TransactionTrytes> trnTrytes;
                        try
                        {
                            trnTrytes = await _repo.GetTrytesAsync(OnlyNewHashes);
                        }
                        catch (Exception)
                        {
                            throw;
                        }

                        var trans = from i in trnTrytes select Transaction.FromTrytes(i);
                        foreach (var item in trans)
                        {
                            resTransactions.Add(item); // converting to transaction object and adding to collection
                            if (item.Timestamp == 0)
                            {
                                _Logger.LogWarning("Null transaction identified");
                            }
                        }                        
                        
                        //Write to partial cache
                        await _DB.AddPartialCacheEntriesAsync(
                            call: callerID,
                            input: OnlyNewHashes.Concat(CachedInput).ToList(),  // input should include all transaction hashes - old ones from cache + new ones from API call
                            results: resTransactions); // Let's store only newly added items to the cache with a new timestamp
                    }

                    // adding original transations from the cache to the output
                    resTransactions.AddRange(CachedOutput);
                }

                _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transactions loaded from cache. {OnlyNewHashes.Count} transactions were new ones.", callerID, CachedOutput.Count, OnlyNewHashes.Count);
                return resTransactions;
            }
            
            public async Task<AddressWithBalances> GetBalanceByAddress(string address)
            {
                // get a list of transactions to the given address
                var callerID = $"GetBalanceByAddress::{address}";

                // Get from a partial cache
                var cached = await _DB.GetPartialCacheEntryAsync(call: callerID);
                var CachedInput = (string)cached.Item1;
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

                    var prevBalance = CachedOutput == null ? -1 : CachedOutput.Addresses[0].Balance;
                    _Logger.LogInformation("{callerID} in action. Balance has been changed from the last time. It was {prevBalance} and now it is {res.Addresses[0].Balance}.", callerID, prevBalance, res.Addresses[0].Balance);
                }                               

                return res;
            }
        }       

    }
}
