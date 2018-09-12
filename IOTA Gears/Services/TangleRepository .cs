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
using IOTAGears.EntityModels;
using Tangle.Net.Utils;
using Tangle.Net.ProofOfWork.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace IOTAGears.Services
{
    public interface ITangleRepository
    {
        ApiTasks Api { get; }
        NodeManager NodeManager { get; }
        DBManager DB { get; }
        ILogger<TangleRepository> Logger { get; }
        TimedBackgroundService TimedBackgroundService { get; }
        string ActualNodeServer { get; }
    }

    public class TangleRepository : ITangleRepository
    {
        public ApiTasks Api { get; }
        public NodeManager NodeManager { get;  }
        public DBManager DB { get; }
        public ILogger<TangleRepository> Logger { get;  }
        public TimedBackgroundService TimedBackgroundService { get; }
        public string ActualNodeServer { get; }
        
        public TangleRepository(ILogger<TangleRepository> logger, INodeManager nodemanager, IDBManager dBManager, IHostedService timedbackgroundservice) {
            NodeManager = (NodeManager)nodemanager;
            Logger = logger;
            DB = (DBManager)dBManager;
            TimedBackgroundService = (TimedBackgroundService)timedbackgroundservice;

            var node = NodeManager.SelectNode(); // TODO: add some smart logic for node selection - round robin?
            ActualNodeServer = node ?? throw new Exception("There is not a NODE to deal with...");

            Api = new ApiTasks(
                InitRestClient(node),
                this
                );
        }
        
        private RestIotaRepository InitRestClient(string node)
        {
            var res = new RestIotaRepository(new RestClient(node) { Timeout=5000});
            Logger.LogInformation("TangleRepository initiated... selected node: {node}", node);
            return res;
        }       
        
    }
    
    public class ApiTasks
    {
        private RestIotaRepository _repo { get; }
        private DBManager _DB { get; }
        private ILogger<TangleRepository> _Logger { get; }
        private string _ActualNodeServer { get; }
        private TimedBackgroundService _TimedBackgroundService { get; }

        public ApiTasks(RestIotaRepository repo, TangleRepository parent)
        {
            _repo = repo;
            _DB = parent.DB;
            _Logger = parent.Logger;
            _ActualNodeServer = parent.ActualNodeServer;
            _TimedBackgroundService = parent.TimedBackgroundService;
        }

        public async Task<NodeInfo> GetNodeInfoAsync()
        {
            NodeInfo res;
            try
            {
                _Logger.LogInformation("Performing external API call GetNodeInfo... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.GetNodeInfoAsync();
                _Logger.LogInformation("External API call... Finished.");
            }
            catch (Exception)
            {
                throw;
            }
            return res;
        }

        private async Task<List<Hash>> GetLatestInclusionStates(string hash, List<Hash> hashes) // non-public function
        {
            if (hashes.Count == 0)
            {
                return null;
            }

            // get a list of confirmed TXs to the given address
            var callerID = $"ConfirmedTransactionsByHash::{hash}";

            // Get from a partial cache - list of confirmed TXs
            var cached = await _DB.GetPartialCacheEntriesAsync(call: callerID);
            var CachedOutput = cached == null ? new List<Hash>() : cached.Cast<Hash>().ToList();

            var OnlyNonConfirmedHashes = hashes.Except(CachedOutput, (a, b) => a.Value == b.Value).ToList();

            InclusionStates res;
            try
            {
                _Logger.LogInformation("Performing external API call GetLatestInclusionStates for {OnlyNonConfirmedHashes.Count} hashes... via node {_ActualNodeServer}", OnlyNonConfirmedHashes.Count, _ActualNodeServer);
                res = await _repo.GetLatestInclusionAsync(OnlyNonConfirmedHashes);
                _Logger.LogInformation("External API call... Finished. Retuned {res.States.Count} states.", res.States.Count);
            }
            catch (Exception)
            {
                throw;
            }

            var OnlyNewlyConfirmedHashes = (from v in res.States where v.Value == true select v.Key).ToList();

            if (OnlyNewlyConfirmedHashes.Count > 0)
            {
                //Write to partial cache
                await _DB.AddPartialCacheEntriesAsync(
                    call: callerID,
                    results: OnlyNewlyConfirmedHashes,
                    identDelegate: (s) => (s as Hash).Value); // Let's store only newly added items to the cache with a new timestamp and so I can check what have been new ones
            }

            _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} confirmed hashes loaded from cache. {OnlyNewlyConfirmedHashes.Count} hashes were new ones.", callerID.Substring(0, 25), CachedOutput.Count, OnlyNewlyConfirmedHashes.Count);

            //returning all from cache + new one from API call
            return CachedOutput.Concat(OnlyNewlyConfirmedHashes).ToList();

        }

        public async Task<TransactionHashList> GetTransactionsByBundle(string bundleHash)
        {
            // get a list of transactions to the given address
            var callerID = $"FindTransactionsByBundle::{bundleHash}";

            // Get from a partial cache
            var cached = await _DB.GetPartialCacheEntriesAsync(call: callerID);
            var CachedOutput = cached == null ? new List<Hash>() : cached.Cast<Hash>().ToList();

            // Get info from node
            TransactionHashList res;
            try
            {
                _Logger.LogInformation("Performing external API call FindTransactionsByBundles for a single address... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.FindTransactionsByBundlesAsync(new List<Hash>() { new Hash(bundleHash) });
                _Logger.LogInformation("External API call... Finished. Retuned {res.Hashes.Count} hashes.", res.Hashes.Count);
            }
            catch (Exception)
            {
                throw;
            }
            var OnlyNewHashes = res.Hashes.Except(CachedOutput, (a, b) => a.Value == b.Value).ToList();
            if (OnlyNewHashes.Count > 0)
            {
                //Write to partial cache
                await _DB.AddPartialCacheEntriesAsync(
                    call: callerID,
                    results: OnlyNewHashes,
                    identDelegate: (s) => (s as Hash).Value); // Let's store only newly added items to the cache with a new timestamp and so I can check what have been new ones
            }

            _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transaction hashes loaded from cache. {OnlyNewHashes.Count} hashes were new ones.", callerID.Substring(0, 50), CachedOutput.Count, OnlyNewHashes.Count);

            res.Hashes = CachedOutput.Concat(OnlyNewHashes).ToList(); //returning all from cache + new one from API call
            return res;
        }

        public async Task<TransactionHashList> GetTransactionsByAddress(string address)
        {
            // get a list of transactions to the given address
            var callerID = $"FindTransactionsByAddress::{address}";

            // Get from a partial cache
            var cached = await _DB.GetPartialCacheEntriesAsync(call: callerID);
            var CachedOutput = cached == null ? new List<Hash>() : cached.Cast<Hash>().ToList();

            // Get info from node
            TransactionHashList res;
            try
            {
                _Logger.LogInformation("Performing external API call FindTransactionsByAddresses for a single address... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.FindTransactionsByAddressesAsync(new List<Address>() { new Address(address) });                                
                _Logger.LogInformation("External API call... Finished. Retuned {res.Hashes.Count} hashes.", res.Hashes.Count);
            }
            catch (Exception)
            {
                throw;
            }
            var OnlyNewHashes = res.Hashes.Except(CachedOutput, (a, b) => a.Value == b.Value).ToList();
            if (OnlyNewHashes.Count > 0)
            {
                //Write to partial cache
                await _DB.AddPartialCacheEntriesAsync(
                    call: callerID,
                    results: OnlyNewHashes,
                    identDelegate: (s) => (s as Hash).Value); // Let's store only newly added items to the cache with a new timestamp and so I can check what have been new ones
            }

            _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transaction hashes loaded from cache. {OnlyNewHashes.Count} hashes were new ones.", callerID.Substring(0, 50), CachedOutput.Count, OnlyNewHashes.Count);

            res.Hashes = CachedOutput.Concat(OnlyNewHashes).ToList(); //returning all from cache + new one from API call
            return res;
        }

        public async Task<List<TransactionContainer>> GetDetailedTransactionsByAddress(string address)
        {
            var callerID = $"FindTransactionsByAddress.Details::{address}";

            TransactionHashList trnList;
            try
            {
                // Get list of all transaction hashes - local API call
                trnList = await this.GetTransactionsByAddress(address);
            }
            catch (Exception)
            {
                throw;
            }

            // GETTING DETAILS

            return await GetTransactionDetails(address, trnList);
        }

        public async Task<List<TransactionContainer>> GetDetailedTransactionByHash(string hash)
        {
            
            TransactionHashList trnList = new TransactionHashList() { Hashes= new List<Hash>(1) };
            trnList.Hashes.Add (new Hash(hash));
            
            // GETTING DETAILS

            return await GetTransactionDetails(hash, trnList);
        }
        
        public async Task<List<TransactionContainer>> GetDetailedTransactionsByBundle(string bundleHash)
        {
            var callerID = $"FindTransactionsByBundle.Details::{bundleHash}";

            TransactionHashList trnList;
            try
            {
                // Get list of all transaction hashes - local API call
                trnList = await this.GetTransactionsByBundle(bundleHash);
            }
            catch (Exception)
            {
                throw;
            }

            // GETTING DETAILS

            return await GetTransactionDetails(bundleHash, trnList);
        }

        private async Task<List<TransactionContainer>> GetTransactionDetails(string hash, TransactionHashList transactionList)
        {
            var callerID = $"FullTransactions::{hash}";

            // Getting from a partial cache to speed up the second call
            var cached = await _DB.GetPartialCacheEntriesAsync(call: callerID);
            var CachedOutput = cached == null ? new List<TransactionContainer>() : cached.Cast<TransactionContainer>().ToList();

            // list of TX hashes returned from cache and its confirmation status
            Dictionary<Hash, bool?> CachedTXHashes = (from i in CachedOutput select new { i.Transaction.Hash, i.IsConfirmed }).ToDictionary(a => a.Hash, b => b.IsConfirmed);

            // list of TX hashes that are non confirmed so far (including those from cache)
            var nonConfirmedTXs = transactionList.Hashes.Except(
                (from v in CachedTXHashes where v.Value == true select v.Key).ToList(),
                (a, b) => a.Value == b.Value)
                .ToList();

            // list of transaction hashes that are new (not in the cache)
            var OnlyNewHashes = transactionList.Hashes.Except(CachedTXHashes.Keys, (a, b) => a.Value == b.Value).ToList();

            // getting confirmation status - independent branch 1
            List<Hash> confirmed;
            confirmed = await this.GetLatestInclusionStates(hash, nonConfirmedTXs); // get list of confirmed TXs out of those non-confirmed so far

            // independent branch 2
            var resTransactions = new List<TransactionContainer>();
            if (transactionList.Hashes.Count > 0) // are there any TXs?
            {
                if (OnlyNewHashes.Count > 0) // any new ones to get info about?
                {
                    List<TransactionTrytes> trnTrytes;
                    trnTrytes = await this.GetTrytesAsync(OnlyNewHashes);

                    var trans = (from i in trnTrytes
                                 where i.TrytesLength == 2673 && !string.IsNullOrEmpty(i.GetChunk(2322, 9).Value.Replace("9", "", StringComparison.InvariantCulture))
                                 select new TransactionContainer(i)).ToList(); // Converting trytes to transaction objects
                    foreach (var item in trans)
                    {
                        if (item.Transaction.Timestamp != 0) // if node returned no info about TX, then skip it. It is possible if TX hash was saved in cache from permanode but trytes are getting from normal node
                        {
                            if (confirmed.Exists(e => e.Value == item.Transaction.Hash.Value))
                            {
                                item.IsConfirmed = true;
                            }
                            else
                            {
                                item.IsConfirmed = false;
                            }

                            resTransactions.Add(item);
                        }
                    }

                    //Write to partial cache
                    await _DB.AddPartialCacheEntriesAsync(
                        call: callerID,
                        results: resTransactions,
                        identDelegate: (a) => (a as TransactionContainer).Transaction.Hash.Value, // this a function that provide additional ident for each entry
                        EntityTimestampDelegate: (a) => (a as TransactionContainer).Transaction.Timestamp
                        ); // Let's store only newly added TXs to the cache with a new timestamp
                }

                // Updating cache entries of those newly confirmed transactions that were in cache
                var CacheEntriesToBeUpdated = (from t in CachedOutput
                                               where t.IsConfirmed == false && confirmed.Exists(e => e.Value == t.Transaction.Hash.Value)
                                               select t).Select(x => { x.IsConfirmed = true; return x; }).ToList();

                if (CacheEntriesToBeUpdated.Count > 0)
                {
                    //Write to partial cache
                    await _DB.AddPartialCacheEntriesAsync(
                        call: callerID,
                        results: CacheEntriesToBeUpdated,
                        identDelegate: (a) => (a as TransactionContainer).Transaction.Hash.Value,
                        EntityTimestampDelegate: (a) => (a as TransactionContainer).Transaction.Timestamp
                        );
                }

                // adding original transations from the cache to the output
                resTransactions.AddRange(CachedOutput);
            }

            _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transactions loaded from cache. {OnlyNewHashes.Count} transactions were new ones.", callerID.Substring(0, 50), CachedOutput.Count, OnlyNewHashes.Count);
            return resTransactions;
        }

        private async Task<List<TransactionTrytes>> GetTrytesAsync(List<Hash> OnlyNewHashes)
        {
            if (OnlyNewHashes.Count == 0)
            {
                return null;
            }

            List<TransactionTrytes> trnTrytes;
            try
            {
                _Logger.LogInformation("Performing external API calls GetTrytes for {OnlyNewHashes.Count} transactions... via node {_ActualNodeServer}", OnlyNewHashes.Count, _ActualNodeServer);
                trnTrytes = await _repo.GetTrytesAsync(OnlyNewHashes); // get info about TXs
                _Logger.LogInformation("External API call... Finished. Returned {trnTrytes.Count} trytes.", trnTrytes.Count);
            }

            catch (Exception)
            {
                throw;
            }

            return trnTrytes;
        }

        public async Task<AddressWithBalances> GetBalanceByAddress(string address)
        {
            // get a list of transactions to the given address
            var callerID = $"GetBalanceByAddress::{address}";

            // Get from a partial cache
            var cached = await _DB.GetPartialCacheEntryAsync(call: callerID);
            var CachedOutput = (AddressWithBalances)cached;

            // Get info from node
            AddressWithBalances res;
            try
            {
                _Logger.LogInformation("Performing external API call GetBalances for a single address... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.GetBalancesAsync(new List<Tangle.Net.Entity.Address>() { new Tangle.Net.Entity.Address(address) });
                _Logger.LogInformation("External API call... Finished");
            }
            catch (Exception)
            {
                throw;
            }

            if (CachedOutput == null || CachedOutput.Addresses[0].Balance != res.Addresses[0].Balance) // balance has been changed - adding new one with a new timestamp
            {
                //Write to partial cache
                await _DB.AddPartialCacheEntryAsync(
                    call: callerID,
                    ident: address,
                    timestamp: 0,
                    result: res); // Let's add actual balance

                var prevBalance = CachedOutput == null ? -1 : CachedOutput.Addresses[0].Balance;
                _Logger.LogInformation("{callerID} in action. Balance has changed from the last time. It was {prevBalance} and now it is {res.Addresses[0].Balance}.", callerID.Substring(0, 50), prevBalance, res.Addresses[0].Balance);
            }

            return res;
        }



        //public async Task<Bundle> GetBundleByTransaction(string hash)
        //{
        //    // get a list of transactions to the given address
        //    var callerID = $"GetBundleByTransaction::{hash}";

        //    // Get from a partial cache
        //    var cached = await _DB.GetPartialCacheEntryAsync(call: callerID);
        //    var CachedOutput = (Bundle)cached;

        //    // Get info from node

        //    Bundle res;

        //    if (CachedOutput==null)
        //    {
        //        try
        //        {
        //            _Logger.LogInformation("Performing external API call GetBundleByTransaction for a single hash... via node {_ActualNodeServer}", _ActualNodeServer);
        //            res = await _repo.GetBundleAsync(new Hash(hash));

        //            _Logger.LogInformation("External API call... Finished");
        //        }
        //        catch (Exception)
        //        {
        //            throw;
        //        }

        //        //Write to partial cache
        //        await _DB.AddPartialCacheEntryAsync(
        //                call: callerID,
        //                ident: hash,
        //                timestamp: 0,
        //                result: res);
        //    }
        //    else
        //    {
        //        return CachedOutput;
        //    }            

        //    return res;
        //}



        public async Task<PipelineStatus> AddTransactionToPipeline(string address, string message, HttpRequest request)
        {
            var task = "SendTX";
            var guid = Guid.NewGuid().ToString(); // unique ID of the task
            var ip = request.HttpContext.Connection.RemoteIpAddress.ToString();
            
            // todo: abuse check 

            var numberTasks = await _DB.AddTaskEntryAsync(task, new TaskEntryInput() { Address = address, Message = message, Tag = "IO9GATEWAY9CLOUD" }, ip, guid); //save the given prepared bundle to the pipeline
            if (numberTasks<0)
            {
                _Logger.LogInformation("Abuse usage in affect. IP: {ip}", ip);
                return new PipelineStatus() { Status = StatusDetail.TooManyRequests }; // returning
            }

            if (!_TimedBackgroundService.ProcessingTasksActive)
            { // start processing pipeline
                _TimedBackgroundService.StartProcessingPipeline();
            }

            return new PipelineStatus() { Status = StatusDetail.TaskWasAddedToPipeline, GlobalId = guid, NumberOfRequests=numberTasks };
        }

    }
}
