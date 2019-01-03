using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.Services
{
    public interface INodeManager
    {
#pragma warning disable CA2227 // Collection properties should be read only
         List<string> Nodes { get; set; }
         List<string> StartupNodes { get; }
    }

    public class NodeManager : INodeManager
    {
        public List<string> Nodes { get; set; }
        public List<string> StartupNodes { get; }
        public List<string> POWStartupNodes { get; }
        public List<string> POWNodes { get; set; }
        public int LatestMilestone { get; private set; } = -1;

#pragma warning restore CA2227 // Collection properties should be read only

        private Logger<NodeManager>  Logger { get; set; }

        public NodeManager(IConfiguration configuration, ILogger<NodeManager> logger)
        {
            var nodes = (from i in configuration.AsEnumerable() where i.Key.StartsWith("IOTANodes:", System.StringComparison.InvariantCultureIgnoreCase) select i.Value).ToList();
            var pownodes = (from i in configuration.AsEnumerable() where i.Key.StartsWith("POWnodes:", System.StringComparison.InvariantCultureIgnoreCase) select i.Value).ToList();

            Nodes = nodes;
            POWNodes = pownodes;

            StartupNodes = nodes;
            POWStartupNodes = pownodes;

            Logger = (Logger<NodeManager>)logger;

            if (Logger!=null)
            {
                Logger.LogInformation("NodeManager initialized...");
                Logger.LogInformation("Using the following nodes: {nodes}", Nodes);
                Logger.LogInformation("Using the following POW nodes: {pownodes}", pownodes);
            }            
        }

        public Dictionary<string, bool> PerformPOWHealthCheck()
        {
            // health check of POW nodes
            if (Logger != null)
            {
                Logger.LogInformation("Performing health check of POW NODES...");
            }

            var stats = new Dictionary<string, bool>();            
            foreach (var node in this.POWStartupNodes) // always starts with all original nodes
            {
                var client = new RestSharp.RestClient(node) { Timeout = 1000 };
                var request = new RestSharp.RestRequest(node, RestSharp.Method.OPTIONS);
                //request.AddJsonBody(new { command = "attachToTangle" });
                var resp = client.Execute(request);               

                if (resp.ResponseStatus==RestSharp.ResponseStatus.Completed && resp.StatusCode==System.Net.HttpStatusCode.NoContent)
                {
                    stats.Add(node, true);
                    Logger.LogInformation("POW node {node} is healthy!", node);
                }
                else
                {
                    Logger.LogInformation("Error while checking POW node {node}. Error: {resp.StatusDescription}", node, resp.StatusDescription);
                    stats.Add(node, false);
                }
            }            
            return stats;
        }
        
        public Dictionary<string, bool> PerformHealthCheck()
        {
            // health check of general purpose nodes
            Logger.LogInformation("Performing health check of NODES...");            

            var stats = new Dictionary<string, Tangle.Net.Repository.DataTransfer.NodeInfo>();
            foreach (var node in this.StartupNodes) // always starts with all original nodes
            {
                var repo = new Tangle.Net.Repository.RestIotaRepository(
                    new RestSharp.RestClient(node) { Timeout = 1000 } // the node should answer in one minute otherwise timeout
                );

                Tangle.Net.Repository.DataTransfer.NodeInfo ninfo;
                try
                {
                    ninfo = repo.GetNodeInfo();
                }
                catch (Exception e)
                {
                    ninfo = null;
                    Logger.LogInformation("Error while checking {node}. Error: {e.Message} Inner Exception: {e.InnerException?.Message}", node, e.Message, e.InnerException?.Message);
                }               

                stats.Add(node, ninfo);                
            }

            var res = new Dictionary<string, bool>();
            var maxMilestoneIndex = stats.Values.Max((x) => x==null ? -1 : x.LatestMilestoneIndex);
            foreach (var node in stats)
            {
                if (node.Value!=null && (maxMilestoneIndex - node.Value.LatestMilestoneIndex) < 3 &&
                    Math.Abs(node.Value.LatestMilestoneIndex - node.Value.LatestSolidSubtangleMilestoneIndex) < 3 &&
                    node.Value.Neighbors > 2)
                {
                    res.Add(node.Key, true);
                    if (Logger != null)
                    {
                        Logger.LogInformation("{node.Key} is healthy! LatestMilestoneIndex: {node.Value.LatestMilestoneIndex}", node.Key, node.Value.LatestMilestoneIndex);
                    }
                }
                else
                {
                    res.Add(node.Key, false);
                    if (Logger != null)
                    {
                        Logger.LogInformation("{node.Key} is in bad condition!", node.Key);
                    }
                }
            }
            if (this.LatestMilestone>=maxMilestoneIndex)
            {
                Logger.LogError("Milestone index is not moving!");
            }
            else
            {
                this.LatestMilestone = maxMilestoneIndex; //update latest milestone index
            }
            
            return res;
        }
        
        public string SelectNode()
        {
            var nct = Nodes.Count;
            if (nct == 0) { return null; }

            return Nodes[new Random().Next(0, nct)]; // random node to be used ATM
        }

        public string SelectPOWNode()
        {
            var nct = POWNodes.Count;
            if (nct == 0) { return null; }

            return POWNodes[new Random().Next(0, nct)]; // random POW node to be used ATM
        }

    }

}
