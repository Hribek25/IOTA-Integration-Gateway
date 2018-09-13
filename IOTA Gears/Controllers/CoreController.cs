using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Tangle.Net.Repository.DataTransfer;
using IOTAGears.Services;
using Microsoft.Extensions.Logging;
using IOTAGears.ActionFilters;
using System.Net;
using IOTAGears.EntityModels;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;
using RestSharp;
using Newtonsoft.Json.Linq;

namespace IOTAGears.Controllers
{    
    [Route("api/[controller]")]
    public class CoreController : Controller
    {
        private readonly TangleRepository _repository;
        private readonly ILogger<CoreController> _logger;
        
        //CTOR
        public CoreController(ITangleRepository repo, ILogger<CoreController> logger) // dependency injection
        {
            _repository = (TangleRepository)repo;
            _logger = logger;
            
        }
        //CTOR
        
        // GET api/core/status
        /// <summary>
        /// Gateway status
        /// </summary>
        /// <returns></returns>        
        [HttpGet("[action]")]
        [CacheTangleResponse(
            LifeSpan = 30,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]        
        [ProducesResponseType(typeof(GatewayStatus), (int)HttpStatusCode.OK)]
        public IActionResult Status()
        {
            var res = new GatewayStatus() { Status=StatusEnum.OK, Version=Program.AppVersion() };
            return Json(res); // Format the output
        }


        // GET api/core/ApiMapCalls
        /// <summary>
        /// Summary of avaiable API calls in a structured format
        /// </summary>
        /// <returns></returns>        
        [HttpGet("[action]")]
        [CacheTangleResponse(
            LifeSpan = 86000,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType(typeof(NodeTree), (int)HttpStatusCode.OK)]        
        public IActionResult ApiMapCalls()
        {
            // var TargetURL = Request.Scheme + "://" + Request.Host.ToString() + Program.SwaggerJSONFile();
            var TargetURL = Program.DefaultPublicFacingHttpProtocol() + Request.Host.ToString() + Program.SwaggerJsonFile(); // TODO: Move the method to conf file
            _logger.LogInformation("Trying to get API definition from {TargetURL}", TargetURL);

            var client = new RestSharp.RestClient(TargetURL) { Timeout = 2000 };
            var resp = client.Execute(new RestSharp.RestRequest(TargetURL, RestSharp.Method.GET));

            if (resp.IsSuccessful && resp.StatusCode==HttpStatusCode.OK)
            {
                var Source = JsonConvert.DeserializeObject<JObject>(resp.Content);
                var root = new NodeTree("Gateway");
                foreach (var item in Source["paths"]) // let's cycle thru all API paths
                {
                    var keys = (item as JProperty).Name.Split("/").Where(a=>!string.IsNullOrWhiteSpace(a)); //split path into components
                    var node = root; //tree should start from root
                    foreach (var v in keys)
                    {
                        var entry = node.GetChild(v); // does the given component exist within children nodes?
                        if (entry == null) //let' create new node
                        {
                            var newnode = new NodeTree(v);
                            node.Children.Add(newnode);
                            node = newnode; // new node is a new starting point
                        }
                        else
                        {
                            node = entry; // found node is new starting point
                        }
                    }
                    // here is the last node and so changing it to method
                    node.Name += "()";
                }
                return Json(root); // Format the output
            }
            return StatusCode(500);
        }
    }
}
