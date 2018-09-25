﻿using Microsoft.Extensions.Configuration;
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
        Logger<TangleRepository> Logger { get; }
        TimedBackgroundService TimedBackgroundService { get; }
        string ActualNodeServer { get; }
    }

    public class TangleRepository : ITangleRepository
    {
        public ApiTasks Api { get; }
        public NodeManager NodeManager { get; }
        public DBManager DB { get; }
        public Logger<TangleRepository> Logger { get; }
        public TimedBackgroundService TimedBackgroundService { get; }
        public string ActualNodeServer { get; }

        public TangleRepository(ILogger<TangleRepository> logger, INodeManager nodemanager, IDBManager dBManager, IHostedService timedbackgroundservice)
        {
            NodeManager = (NodeManager)nodemanager;
            Logger = (Logger<TangleRepository>)logger;
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
            var res = new RestIotaRepository(new RestClient(node) { Timeout = 10000 });
            Logger.LogInformation("TangleRepository initiated... selected node: {node}", node);
            return res;
        }

    }

    public class ApiTasks
    {
        private RestIotaRepository _repo { get; }
        private DBManager _DB { get; }
        private Logger<TangleRepository> _Logger { get; }
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

        private async Task<HashSet<string>> GetLatestInclusionStates(string hash, HashSet<string> hashes) // non-public function
        {
            if (hashes.Count == 0)
            {
                return null;
            }

            // get a list of confirmed TXs to the given address
            var callerID = $"ConfirmedTransactionsByHash::{hash}";

            // Get from a partial cache - list of confirmed TX hashes
            var cached = await _DB.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = cached == null ? new HashSet<string>() : (HashSet<string>)cached;
            var loadedfromcache = CachedOutput.Count();
            hashes.ExceptWith(CachedOutput); // removing all hashes that were already confirmed

            InclusionStates res;
            HashSet<string> OnlyNewlyConfirmedHashes = null;
            if (hashes.Count > 0) // are there any non-confirmed left?
            {
                try
                {
                    _Logger.LogInformation("Performing external API call GetLatestInclusionStates for {hashes.Count} hashes... via node {_ActualNodeServer}", hashes.Count, _ActualNodeServer);
                    res = await _repo.GetLatestInclusionAsync((from h in hashes select new Hash(h)).ToList());
                    _Logger.LogInformation("External API call... Finished. Retuned {res.States.Count} states.", res.States.Count);
                }
                catch (Exception e)
                {
                    _Logger.LogError("External API call... Failed. Error: {e.Message}, Inner Error: {e.InnerException.Message}", e.Message, e.InnerException?.Message);
                    throw;
                }

                OnlyNewlyConfirmedHashes = (from v in res.States where v.Value == true select v.Key.Value).ToHashSet();

                if (OnlyNewlyConfirmedHashes.Count > 0)
                {
                    CachedOutput.UnionWith(OnlyNewlyConfirmedHashes); // saving all back to the cache
                    //Write to partial cache
                    await _DB.AddFSPartialCacheEntryAsync(
                        call: callerID,
                        result: CachedOutput);
                }
            }

            _Logger.LogInformation("{callerID} in action. {loadedfromcache} confirmed hashes loaded from cache. {OnlyNewlyConfirmedHashes.Count} hashes were new ones.", callerID.Substring(0, 25), loadedfromcache, OnlyNewlyConfirmedHashes?.Count);

            //returning all from cache + new one from API call
            return CachedOutput;

        }

        public async Task<HashSet<string>> GetTransactionsByBundle(string bundleHash)
        {
            // get a list of transactions to the given address
            var callerID = $"FindTransactionsByBundle::{bundleHash}";

            // Get from a partial cache
            var cached = await _DB.GetFSPartialCacheEntryAsync(call: callerID);
            HashSet<string> CachedOutput = cached == null ? new HashSet<string>() : (HashSet<string>)cached;

            // Get info from node
            TransactionHashList res;
            try
            {
                _Logger.LogInformation("Performing external API call FindTransactionsByBundles for a single address... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.FindTransactionsByBundlesAsync(new List<Hash>() { new Hash(bundleHash) });
                _Logger.LogInformation("External API call... Finished. Retuned {res.Hashes.Count} hashes.", res.Hashes.Count);
            }
            catch (Exception e)
            {
                _Logger.LogError("External API call... Failed. Error: {e.Message}, Inner Error: {e.InnerException.Message}", e.Message, e.InnerException?.Message);
                throw;
            }

            var origCnt = CachedOutput.Count;
            CachedOutput.UnionWith((from i in res.Hashes select i.Value).ToArray());

            if (origCnt < CachedOutput.Count) // need to update the record
            {
                //Write to partial cache
                await _DB.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: CachedOutput);
            }

            _Logger.LogInformation("{callerID} in action. {origCnt} transaction hashes loaded from cache. {CachedOutput.Count} hashes in total.", callerID.Substring(0, 50), origCnt, CachedOutput.Count);

            return CachedOutput;
        }

        public async Task<HashSet<string>> GetTransactionsByAddress(string address)
        {
            // get a list of transactions to the given address
            var callerID = $"FindTransactionsByAddress::{address}";

            // Get from a partial cache
            var cached = await _DB.GetFSPartialCacheEntryAsync(call: callerID);
            HashSet<string> CachedOutput = cached == null ? new HashSet<string>() : (HashSet<string>)cached;

            // Get info from node
            TransactionHashList res;
            try
            {
                _Logger.LogInformation("Performing external API call FindTransactionsByAddresses for a single address... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.FindTransactionsByAddressesAsync(new List<Address>() { new Address(address) });
                _Logger.LogInformation("External API call... Finished. Retuned {res.Hashes.Count} hashes.", res.Hashes.Count);
            }
            catch (Exception e)
            {
                _Logger.LogError("External API call... Failed. Error: {e.Message}, Inner Error: {e.InnerException.Message}", e.Message, e.InnerException?.Message);
                throw;
            }

            var origCnt = CachedOutput.Count;
            CachedOutput.UnionWith((from i in res.Hashes select i.Value).ToArray());

            if (origCnt < CachedOutput.Count) // need to update the record
            {
                //Write to partial cache
                await _DB.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: CachedOutput);
            }
            _Logger.LogInformation("{callerID} in action. {origCnt} transaction hashes loaded from cache. {CachedOutput.Count} hashes in total.", callerID.Substring(0, 50), origCnt, CachedOutput.Count);

            return CachedOutput; //returning all from cache + new one from API call
        }

        public async Task<HashSet<TransactionContainer>> GetDetailedTransactionsByAddress(string address)
        {
            var callerID = $"FindTransactionsByAddress.Details::{address}";

            HashSet<string> trnList;
            try
            {
                trnList = await this.GetTransactionsByAddress(address); // Get list of all transaction hashes - local API call
            }
            catch (Exception)
            {
                throw;
            }

            // GETTING DETAILS

            return await GetTransactionDetails(address, trnList);
        }

        public async Task<HashSet<TransactionContainer>> GetDetailedTransactionByHash(string hash)
        {
            HashSet<string> trnList = new HashSet<string>() { hash };

            // GETTING DETAILS

            return await GetTransactionDetails(hash, trnList);
        }

        public async Task<HashSet<TransactionContainer>> GetDetailedTransactionsByBundle(string bundleHash)
        {
            var callerID = $"FindTransactionsByBundle.Details::{bundleHash}";

            HashSet<string> trnList;
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

        private async Task<HashSet<TransactionContainer>> GetTransactionDetails(string hash, HashSet<string> transactionList)
        {
            var callerID = $"FullTransactions::{hash}";

            // Getting from a partial cache to speed up the second call
            var cached = await _DB.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = cached == null ? new HashSet<TransactionContainer>() : (HashSet<TransactionContainer>)cached;

            // list of non confirmed TX hashes returned from cache
            HashSet<string> CachedTXHashesNonConfirmed = (from i in CachedOutput where i.IsConfirmed == false select i.Transaction.Hash.Value).ToHashSet();

            // list of transaction hashes that are only new and not in the cache
            transactionList.ExceptWith(from v in CachedOutput select v.Transaction.Hash.Value);

            // total list of TX hashes that have not been confirmed so far (incl. cache)
            CachedTXHashesNonConfirmed.UnionWith(transactionList);
            
            // getting confirmation status - independent branch 1
            HashSet<string> confirmed;
            confirmed = await this.GetLatestInclusionStates(hash, CachedTXHashesNonConfirmed); // get list of confirmed TXs out of those non-confirmed so far


            // independent branch 2
            var resTransactions = new HashSet<TransactionContainer>(); // final collection of transactions to be returned
            if (transactionList.Count > 0) // are there any TXs to get info about?
            {
                List<TransactionTrytes> trnTrytes;
                trnTrytes = await this.GetTrytesAsync(transactionList);

                HashSet<TransactionContainer> trans = (from i in trnTrytes
                                                       where i.TrytesLength == 2673 && !string.IsNullOrEmpty(i.GetChunk(2322, 9).Value.Replace("9", "", StringComparison.InvariantCulture))
                                                       select new TransactionContainer(i)).ToHashSet(); // Converting trytes to transaction objects - only for non-empty transactions
                foreach (var item in trans)
                {
                    if (item.Transaction.Timestamp != 0) // if node returned no info about TX, then skip it. It is possible if TX hash was saved in cache from permanode but trytes are getting from normal node
                    {
                        if (!(confirmed is null) && confirmed.Contains(item.Transaction.Hash.Value))
                        {
                            item.IsConfirmed = true;
                        }
                        else
                        {
                            item.IsConfirmed = false;
                        }

                        resTransactions.Add(item); // newly processed TXs are being added to the final collection
                    }
                }
            }

            // adding cache entries to the final list

            
            foreach (var item in CachedOutput)
            {
                if (!(confirmed is null) && confirmed.Contains(item.Transaction.Hash.Value)) // updating also transactions in cache in case they were confirmed lately
                {
                    item.IsConfirmed = true;
                }
                resTransactions.Add(item);
            }

            if (resTransactions.Count>0) //TODO: add some clever condition whether it has been changed or not
            {
                //Write to partial cache
                await _DB.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: resTransactions);
            }            

            _Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transactions loaded from cache. {transactionList.Count} transactions were new ones.", callerID.Substring(0, 50), CachedOutput.Count, transactionList.Count);
            return resTransactions;
        }

        private async Task<List<TransactionTrytes>> GetTrytesAsync(HashSet<string> OnlyNewHashes)
        {
            if (OnlyNewHashes.Count == 0)
            {
                return null;
            }

            List<TransactionTrytes> trnTrytes;
            try
            {
                _Logger.LogInformation("Performing external API calls GetTrytes for {OnlyNewHashes.Count} transactions... via node {_ActualNodeServer}", OnlyNewHashes.Count, _ActualNodeServer);
                trnTrytes = await _repo.GetTrytesAsync((from t in OnlyNewHashes select new Hash(t)).ToList()); // get info about TXs
                _Logger.LogInformation("External API call... Finished. Returned {trnTrytes.Count} trytes.", trnTrytes.Count);
            }

            catch (Exception e)
            {
                _Logger.LogError("External API call... Failed. Error: {e.Message}, Inner Error: {e.InnerException.Message}", e.Message, e.InnerException?.Message);
                throw;
            }

            return trnTrytes;
        }

        public async Task<AddressWithBalances> GetBalanceByAddress(string address)
        {
            // get a list of transactions to the given address
            var callerID = $"GetBalanceByAddress::{address}";

            // Get from a partial cache
            var cached = await _DB.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = (AddressWithBalances)cached;

            // Get info from node
            AddressWithBalances res;
            try
            {
                _Logger.LogInformation("Performing external API call GetBalances for a single address... via node {_ActualNodeServer}", _ActualNodeServer);
                res = await _repo.GetBalancesAsync(new List<Tangle.Net.Entity.Address>() { new Tangle.Net.Entity.Address(address) });
                _Logger.LogInformation("External API call... Finished");
            }
            catch (Exception e)
            {
                _Logger.LogError("External API call... Failed. Error: {e.Message}, Inner Error: {e.InnerException.Message}", e.Message, e.InnerException?.Message);
                throw;
            }

            if (CachedOutput == null || CachedOutput.Addresses[0].Balance != res.Addresses[0].Balance) // balance has been changed - adding new one with a new timestamp
            {
                //Write to partial cache
                await _DB.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: res); // Let's add actual balance

                var prevBalance = CachedOutput == null ? -1 : CachedOutput.Addresses[0].Balance;
                _Logger.LogInformation("{callerID} in action. Balance has changed from the last time. It was {prevBalance} and now it is {res.Addresses[0].Balance}.", callerID.Substring(0, 50), prevBalance, res.Addresses[0].Balance);
            }

            return res;
        }

        public async Task<Bundle> GetBundleByTransaction(string hash)
        {
            // get a list of transactions to the given address
            var callerID = $"GetBundleByTransaction::{hash}";

            // Get from a partial cache
            var cached = await _DB.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = (Bundle)cached;

            // Get info from node

            Bundle res;

            if (CachedOutput == null)
            {
                try
                {
                    _Logger.LogInformation("Performing external API call GetBundleByTransaction for a single hash... via node {_ActualNodeServer}", _ActualNodeServer);
                    res = await _repo.GetBundleAsync(new Hash(hash));

                    _Logger.LogInformation("External API call... Finished");
                }
                catch (Exception e)
                {
                    _Logger.LogError("External API call... Failed. Error: {e.Message}, Inner Error: {e.InnerException.Message}", e.Message, e.InnerException?.Message);
                    throw;
                }

                //Write to partial cache
                await _DB.AddFSPartialCacheEntryAsync(
                        call: callerID,
                        result: res);
                return res;
            }
            else
            {
                return CachedOutput;
            }
        }

        public async Task<PipelineStatus> AddTransactionToPipeline(string address, string message, HttpRequest request)
        {
            var task = "SendTX";
            var guid = Guid.NewGuid().ToString(); // unique ID of the task
            var ip = request.HttpContext.Connection.RemoteIpAddress.ToString();

            // todo: abuse check 

            var numberTasks = await _DB.AddDBTaskEntryAsync(task, new TaskEntryInput() { Address = address, Message = message, Tag = "IO9GATEWAY9CLOUD" }, ip, guid); //save the given prepared bundle to the pipeline
            if (numberTasks < 0)
            {
                _Logger.LogInformation("Abuse usage in affect. IP: {ip}", ip);
                return new PipelineStatus() { Status = StatusDetail.TooManyRequests }; // returning
            }

            if (!_TimedBackgroundService.ProcessingTasksActive)
            { // start processing pipeline
                _TimedBackgroundService.StartProcessingPipeline();
            }

            return new PipelineStatus() { Status = StatusDetail.TaskWasAddedToPipeline, GlobalId = guid, NumberOfRequests = numberTasks };
        }

    }
}
