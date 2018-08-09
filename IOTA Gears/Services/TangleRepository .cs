using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestSharp;
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Tangle.Net.Repository;
using Tangle.Net.Repository.DataTransfer;

namespace IOTA_Gears.Services
{
    public interface ITangleRepository
    {
        
    }

    public class TangleRepository : ITangleRepository
    {
        public ApiTasks Api { get; }
        private NodeManager NodeManager { get;  }
        private ILogger<TangleRepository> Logger { get;  } 
        
        public TangleRepository(NodeManager nodemanager, ILogger<TangleRepository> logger) {
            NodeManager = nodemanager;
            Logger = logger;

            var node = NodeManager.SelectNode(); // TODO: add some smart logic for node selection - round robin?
            Api = new ApiTasks(
                InitRestClient(node)
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
            public RestIotaRepository _repo { get; set; }

            public ApiTasks(RestIotaRepository repo)
            {
                _repo = repo;
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


        }              

    }

}
