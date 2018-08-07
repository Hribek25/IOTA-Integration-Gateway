using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IOTA_Gears.Services
{
    public interface INodeManager
    {
        
    }

    public class NodeManager : INodeManager
    {
        private List<string> Nodes { get; set; }
        private ILogger<NodeManager>  Logger { get; set; }

        public NodeManager(List<string> nodes, ILogger<NodeManager> logger)
        {
            Nodes = (List<String>)nodes;            
            Logger = logger;

            Logger.LogInformation("NodeManager initialized...");
            Logger.LogInformation("Using the following nodes: {nodes}", Nodes);
        }

        public string SelectNode()
        {
            var nct = Nodes.Count;
            return Nodes[new Random().Next(0, nct)]; // random node to be used ATM
        }       

    }

}
