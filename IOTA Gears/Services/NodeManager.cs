using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTA_Gears.Services
{
    public interface INodeManager
    {
        
    }

    public class NodeManager : INodeManager
    {
        public List<string> Nodes { get; set; }
        public List<string> StartupNodes { get; }
        private ILogger<NodeManager>  Logger { get; set; }

        public NodeManager(IConfiguration configuration, ILogger<NodeManager> logger)
        {
            var nodes = (from i in configuration.AsEnumerable() where i.Key.StartsWith("IOTANodes:") select i.Value).ToList();
            Nodes = nodes;
            StartupNodes = nodes;

            Logger = logger;

            if (Logger!=null)
            {
                Logger.LogInformation("NodeManager initialized...");
                Logger.LogInformation("Using the following nodes: {nodes}", Nodes);
            }            
        }

        public async Task<Dictionary<string, bool>> PerformHealthCheckAsync()
        {
            if (Logger != null)
            {
                Logger.LogInformation("Performing health check of NODES...");
            }

            var stats = new Dictionary<string, Tangle.Net.Repository.DataTransfer.NodeInfo>();
            foreach (var node in this.StartupNodes) // always starts with all original nodes
            {
                var repo = new Tangle.Net.Repository.RestIotaRepository(
                    new RestSharp.RestClient(node) { Timeout = 1000 }
                );

                Tangle.Net.Repository.DataTransfer.NodeInfo ninfo;
                try
                {
                    ninfo = await repo.GetNodeInfoAsync();
                }
                catch (Exception)
                {
                    ninfo = null;
                }

                if (ninfo!=null)
                {
                    stats.Add(node, ninfo);
                }                                
            }

            var res = new Dictionary<string, bool>();
            if (stats.Count > 0)
            {
                var maxMilestoneIndex = stats.Values.Max((x) => x.LatestMilestoneIndex);
                foreach (var node in stats)
                {
                    if ((maxMilestoneIndex - node.Value.LatestMilestoneIndex) < 3 &&
                        (Math.Abs(node.Value.LatestMilestoneIndex - node.Value.LatestSolidSubtangleMilestoneIndex)) < 3 &&
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
            }
            return res;
        }
        
        public string SelectNode()
        {
            var nct = Nodes.Count;
            if (nct == 0) { return null; }

            return Nodes[new Random().Next(0, nct)]; // random node to be used ATM
        }       

    }

}
