using Microsoft.Extensions.Logging;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tangle.Net.Repository;
using Tangle.Net.Repository.DataTransfer;
using Tangle.Net.Entity;
using IOTAGears.EntityModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace IOTAGears.Services
{
    public interface ITangleRepository
    {
        DbStorageManager DB { get; }
        Logger<TangleRepository> Logger { get; }
        TimedBackgroundService TimedBackgroundService { get; }
        ExternalApiTangleRepository Repo { get; }
        FsStorageManager FS { get; }
    }

    public class TangleRepository : ITangleRepository
    {
        public DbStorageManager DB { get; }
        public FsStorageManager FS { get; }
        public Logger<TangleRepository> Logger { get; }
        public TimedBackgroundService TimedBackgroundService { get; }
        public ExternalApiTangleRepository Repo { get; }

        public TangleRepository(ILogger<TangleRepository> logger, IDbStorageManager dBManager, IHostedService timedbackgroundservice, IExternalApiTangleRepository repository, IFsStorageManager fsStorageManager)
        {
            Logger = (Logger<TangleRepository>)logger;
            DB = (DbStorageManager)dBManager;
            TimedBackgroundService = (TimedBackgroundService)timedbackgroundservice;
            Repo = (ExternalApiTangleRepository)repository;
            FS = (FsStorageManager)fsStorageManager;
        }

        public async Task<NodeInfo> GetNodeInfoAsync()
        {
            NodeInfo res;
            try
            {
                res = await Repo.GetNodeInfoAsync();
            }
            catch (Exception)
            {
                throw;
            }
            return res;
        }

        private async Task<HashSet<string>> GetLatestInclusionStates(string hash, HashSet<string> hashes) // non-public function
        {
            if (hashes.Count == 0) { return null; }

            // get a list of confirmed TXs to the given address
            var callerID = $"{nameof(GetLatestInclusionStates)}::{hash}";

            // Get from a partial cache - list of confirmed TX hashes
            var cached = await FS.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = cached == null ? new HashSet<string>() : (HashSet<string>)cached;
            var loadedfromcache = CachedOutput.Count();
            hashes.ExceptWith(CachedOutput); // removing all hashes that were already confirmed

            InclusionStates res;
            HashSet<string> OnlyNewlyConfirmedHashes = null;
            if (hashes.Count > 0) // are there any non-confirmed left?
            {
                try
                {
                    res = await Repo.GetLatestInclusionAsync((from h in hashes select new Hash(h)).ToList());
                }
                catch (Exception)
                {
                    throw;
                }

                OnlyNewlyConfirmedHashes = (from v in res.States where v.Value == true select v.Key.Value).ToHashSet();

                if (OnlyNewlyConfirmedHashes.Count > 0)
                {
                    CachedOutput.UnionWith(OnlyNewlyConfirmedHashes); // saving all back to the cache
                                                                      //Write to partial cache
                    await FS.AddFSPartialCacheEntryAsync(
                        call: callerID,
                        result: CachedOutput);
                }
            }

            Logger.LogInformation("{callerID} in action. {loadedfromcache} confirmed hashes loaded from cache. {OnlyNewlyConfirmedHashes.Count} hashes were new ones.", callerID.Substring(0, 25), loadedfromcache, OnlyNewlyConfirmedHashes?.Count);

            //returning all from cache + new one from API call
            return CachedOutput;
        }

        public async Task<HashSet<string>> GetTransactionsByBundle(string bundleHash)
        {
            // get a list of transactions to the given address
            var callerID = $"{nameof(GetTransactionsByBundle)}::{bundleHash}";

            // Get from a partial cache
            var cached = await FS.GetFSPartialCacheEntryAsync(call: callerID);
            HashSet<string> CachedOutput = cached == null ? new HashSet<string>() : (HashSet<string>)cached;

            // Get info from node
            TransactionHashList res;
            try
            {
                res = await Repo.FindTransactionsByBundlesAsync(new List<Hash>() { new Hash(bundleHash) });
            }
            catch (Exception)
            {
                throw;
            }

            var origCnt = CachedOutput.Count;
            CachedOutput.UnionWith((from i in res.Hashes select i.Value).ToArray());

            if (origCnt < CachedOutput.Count) // need to update the record
            {
                //Write to partial cache
                await FS.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: CachedOutput);
            }

            Logger.LogInformation("{callerID} in action. {origCnt} transaction hashes loaded from cache. {CachedOutput.Count} hashes in total.", callerID.Substring(0, 50), origCnt, CachedOutput.Count);

            return CachedOutput;
        }

        public async Task<HashSet<string>> GetTransactionsByAddress(string address)
        {
            // get a list of transactions to the given address
            var callerID = $"{nameof(GetTransactionsByAddress)}::{address}";

            // Get from a partial cache
            var cached = await FS.GetFSPartialCacheEntryAsync(call: callerID);
            HashSet<string> CachedOutput = cached == null ? new HashSet<string>() : (HashSet<string>)cached;

            // Get info from node
            TransactionHashList res;
            try
            {
                res = await Repo.FindTransactionsByAddressesAsync(new List<Address>() { new Address(address) });
            }
            catch (Exception)
            {
                throw;
            }

            var origCnt = CachedOutput.Count;
            CachedOutput.UnionWith((from i in res.Hashes select i.Value).ToArray());

            if (origCnt < CachedOutput.Count) // need to update the record
            {
                //Write to partial cache
                await FS.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: CachedOutput);
            }
            Logger.LogInformation("{callerID} in action. {origCnt} transaction hashes loaded from cache. {CachedOutput.Count} hashes in total.", callerID.Substring(0, 50), origCnt, CachedOutput.Count);

            return CachedOutput; //returning all from cache + new one from API call
        }

        public async Task<TxHashSetCollection> GetDetailedTransactionsByAddress(string address)
        {
            var callerID = $"{nameof(GetDetailedTransactionsByAddress)}::{address}";

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
            // Limited to 500 transactions.
            var TooMany = false;

            if (trnList.Count > 500)
            {
                trnList = trnList.Take(500).ToHashSet();
                TooMany = true;
            }

            var res = await this.GetTransactionDetails(address, trnList);

            if (TooMany)
            {
                res.CompleteSet = false; // Indicate that the result is not completed
            }
            return res;
        }

        public async Task<TxHashSetCollection> GetDetailedTransactionByHash(string hash)
        {
            HashSet<string> trnList = new HashSet<string>() { hash };

            // GETTING DETAILS

            return await this.GetTransactionDetails(hash, trnList);
        }

        public async Task<TxHashSetCollection> GetDetailedTransactionsByBundle(string bundleHash)
        {
            var callerID = $"{nameof(GetDetailedTransactionsByBundle)}::{bundleHash}";

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

            return await this.GetTransactionDetails(bundleHash, trnList);
        }

        private async Task<TxHashSetCollection> GetTransactionDetails(string hash, HashSet<string> transactionList)
        {
            var callerID = $"{nameof(GetTransactionDetails)}::{hash}";

            // Getting from a partial cache to speed up the second call
            var cached = await FS.GetFSPartialCacheEntryAsync(call: callerID);
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
            var resTransactions = new TxHashSetCollection(); // final collection of transactions to be returned
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

            if (resTransactions.Count > 0) //TODO: add some clever condition whether it has been changed or not
            {
                //Write to partial cache
                await FS.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: resTransactions);
            }

            Logger.LogInformation("{callerID} in action. {CachedOutput.Count} transactions loaded from cache. {transactionList.Count} transactions were new ones.", callerID.Substring(0, 50), CachedOutput.Count, transactionList.Count);
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
                trnTrytes = await Repo.GetTrytesAsync((from t in OnlyNewHashes select new Hash(t)).ToList()); // get info about TXs                
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
            var callerID = $"{nameof(GetBalanceByAddress)}::{address}";

            // Get from a partial cache
            var cached = await FS.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = (AddressWithBalances)cached;

            // Get info from node
            AddressWithBalances res;
            try
            {
                res = await Repo.GetBalancesAsync(new List<Tangle.Net.Entity.Address>() { new Tangle.Net.Entity.Address(address) });
            }
            catch (Exception)
            {
                throw;
            }
            if (CachedOutput == null || CachedOutput.Addresses[0].Balance != res.Addresses[0].Balance) // balance has been changed - adding new one with a new timestamp
            {
                //Write to partial cache
                await FS.AddFSPartialCacheEntryAsync(
                    call: callerID,
                    result: res); // Let's add actual balance

                var prevBalance = CachedOutput == null ? -1 : CachedOutput.Addresses[0].Balance;
                Logger.LogInformation("{callerID} in action. Balance has changed from the last time. It was {prevBalance} and now it is {res.Addresses[0].Balance}.", callerID.Substring(0, 50), prevBalance, res.Addresses[0].Balance);
            }
            return res;
        }

        public async Task<Bundle> GetBundleByTransaction(string hash)
        {
            // get a list of transactions to the given address
            var callerID = $"{nameof(GetBundleByTransaction)}::{hash}";

            // Get from a partial cache
            var cached = await FS.GetFSPartialCacheEntryAsync(call: callerID);
            var CachedOutput = (Bundle)cached;

            // Get info from node

            Bundle res;
            if (CachedOutput == null)
            {
                try
                {
                    res = await Repo.GetBundleAsync(new Hash(hash));
                }
                catch (Exception)
                {
                    throw;
                }

                //Write to partial cache
                await FS.AddFSPartialCacheEntryAsync(
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

            var numberTasks = await DB.AddDBTaskEntryAsync(task, new TaskEntryInput() { Address = address, Message = message, Tag = "IO9GATEWAY9CLOUD" }, ip, guid); //save the given prepared bundle to the pipeline
            if (numberTasks < 0)
            {
                Logger.LogInformation("Abuse usage in affect. IP: {ip}", ip);
                return new PipelineStatus() { Status = StatusDetail.TooManyRequests }; // returning
            }

            if (!TimedBackgroundService.ProcessingTasksActive)
            { // start processing pipeline
                TimedBackgroundService.StartProcessingPipeline();
            }

            return new PipelineStatus() { Status = StatusDetail.TaskWasAddedToPipeline, GlobalId = guid, NumberOfRequests = numberTasks };
        }
    }
}

