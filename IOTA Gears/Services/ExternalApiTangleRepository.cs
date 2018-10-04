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
    public interface IExternalApiTangleRepository
    {
        NodeManager NodeManager { get; }
        Logger<ExternalApiTangleRepository> Logger { get; }
        RestIotaRepository IotaRepository { get; }
    }

    public class ExternalApiTangleRepository : IExternalApiTangleRepository
    {
        public NodeManager NodeManager { get; }
        public Logger<ExternalApiTangleRepository> Logger { get; }

        public RestIotaRepository IotaRepository { get; private set; }
        public string ActualNodeServer { get; private set; }
        private int NumberOfTrials { get; set; } = 3; // How many times will I try to perform an API call?

        public ExternalApiTangleRepository(ILogger<ExternalApiTangleRepository> logger, INodeManager nodemanager)
        {
            NodeManager = (NodeManager)nodemanager;
            Logger = (Logger<ExternalApiTangleRepository>)logger;
            InitNodeIotaRepository();
            Logger.LogInformation("{nameof(ExternalApiTangleRepository)} initiated... selected node: {node}", nameof(ExternalApiTangleRepository), ActualNodeServer);
        }

        private void InitNodeIotaRepository()
        {
            var node = NodeManager.SelectNode();
            ActualNodeServer = node;

            if (ActualNodeServer is null)
            {
                IotaRepository = null;
            }
            else
            {
                IotaRepository = new RestIotaRepository(new RestClient(node) { Timeout = 5000 });
            }
        }

        internal async Task<NodeInfo> GetNodeInfoAsync(int counter = 0)
        {
            NodeInfo res;
            if (counter > 0) // another trial and so trying to switch the node
            {
                InitNodeIotaRepository();
            }
            Logger.LogInformation("Performing external API call GetNodeInfo... via node {ActualNodeServer}", ActualNodeServer);

            try
            {
                res = await IotaRepository.GetNodeInfoAsync();
            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); } // if there is no IotaRepository due to lack of available nodes
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await GetNodeInfoAsync(++counter); // Performing another call but incrementing counter
                }
            }
            Logger.LogInformation("External API call... Finished.");
            return res;
        }

        internal async Task<InclusionStates> GetLatestInclusionAsync(List<Hash> list, int counter = 0)
        {
            InclusionStates res;
            if (counter > 0) // another trial and so trying to switch the node
            {
                InitNodeIotaRepository();
            }

            Logger.LogInformation("Performing external API call GetLatestInclusionStates for {hashes.Count} hashes... via node {ActualNodeServer}", list.Count, ActualNodeServer);

            try
            {
                res = await IotaRepository.GetLatestInclusionAsync(list);
            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); }
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await GetLatestInclusionAsync(list, ++counter); // Performing another call but incrementing counter
                }
            }

            Logger.LogInformation("External API call... Finished. Retuned {res.States.Count} states.", res.States.Count);
            return res;
        }

        internal async Task<TransactionHashList> FindTransactionsByBundlesAsync(List<Hash> list, int counter = 0)
        {
            TransactionHashList res;
            if (counter > 0)
            {
                InitNodeIotaRepository();
            }
            Logger.LogInformation("Performing external API call FindTransactionsByBundles for a single address... via node {ActualNodeServer}", ActualNodeServer);

            try
            {
                res = await IotaRepository.FindTransactionsByBundlesAsync(list);
            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); }
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await FindTransactionsByBundlesAsync(list, ++counter); // Performing another call but incrementing counter
                }
            }
            Logger.LogInformation("External API call... Finished. Retuned {res.Hashes.Count} hashes.", res.Hashes.Count);
            return res;
        }

        internal async Task<TransactionHashList> FindTransactionsByAddressesAsync(List<Address> list, int counter = 0)
        {
            TransactionHashList res;
            if (counter > 0)
            {
                InitNodeIotaRepository();
            }

            Logger.LogInformation("Performing external API call FindTransactionsByAddresses for a single address... via node {ActualNodeServer}", ActualNodeServer);
            try
            {

                res = await IotaRepository.FindTransactionsByAddressesAsync(list);
            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); }
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await FindTransactionsByAddressesAsync(list, ++counter); // Performing another call but incrementing counter
                }
            }
            Logger.LogInformation("External API call... Finished. Retuned {res.Hashes.Count} hashes.", res.Hashes.Count);
            return res;
        }

        internal async Task<List<TransactionTrytes>> GetTrytesAsync(List<Hash> list, int counter = 0)
        {
            List<TransactionTrytes> trnTrytes;
            if (counter > 0)
            {
                InitNodeIotaRepository();
            }

            Logger.LogInformation("Performing external API calls GetTrytes for {list.Count} transactions... via node {ActualNodeServer}", list.Count, ActualNodeServer);
            try
            {
                trnTrytes = await IotaRepository.GetTrytesAsync(list); // get info about TXs

            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); }
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await GetTrytesAsync(list, ++counter); // Performing another call but incrementing counter
                }
            }
            Logger.LogInformation("External API call... Finished. Returned {trnTrytes.Count} trytes.", trnTrytes.Count);
            return trnTrytes;
        }

        internal async Task<AddressWithBalances> GetBalancesAsync(List<Address> list, int counter = 0)
        {
            AddressWithBalances res;
            if (counter > 0)
            {
                InitNodeIotaRepository();
            }

            Logger.LogInformation("Performing external API call GetBalances for a single address... via node {ActualNodeServer}", ActualNodeServer);
            try
            {
                res = await IotaRepository.GetBalancesAsync(list);
            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); }
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await GetBalancesAsync(list, ++counter); // Performing another call but incrementing counter
                }
            }
            Logger.LogInformation("External API call... Finished");
            return res;
        }

        internal async Task<Bundle> GetBundleAsync(Hash hash, int counter = 0)
        {
            Bundle res;
            if (counter > 0)
            {
                InitNodeIotaRepository();
            }

            Logger.LogInformation("Performing external API call GetBundleByBundleHash for a single hash... via node {ActualNodeServer}", ActualNodeServer);
            try
            {
                res = await IotaRepository.GetBundleAsync(hash);
            }
            catch (NullReferenceException) { throw new Exception("No available nodes to perform the call"); }
            catch (Exception e)
            {
                Logger.LogError("External API call... Failed. Tried {counter} times so far. Error: {e.Message}, Inner Error: {e.InnerException.Message}", counter, e.Message, e.InnerException?.Message);
                if (counter >= NumberOfTrials) // if too many trials
                {
                    throw;
                }
                else
                {
                    return await GetBundleAsync(hash, ++counter); // Performing another call but incrementing counter
                }
            }
            Logger.LogInformation("External API call... Finished");
            return res;
        }
    }
}
