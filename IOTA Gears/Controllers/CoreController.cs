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
using Microsoft.Extensions.Configuration;

namespace IOTAGears.Controllers
{    
    [Route("api/[controller]/[action]")]
    public class CoreController : Controller
    {
        private readonly Logger<CoreController> _logger;
        private readonly IConfiguration _conf;
        
        //CTOR
        public CoreController(ILogger<CoreController> logger, IConfiguration configuration) // dependency injection
        {
            //_repository = (TangleRepository)repo;
            _logger = (Logger <CoreController>)logger;
            _conf = configuration;            
        }
        //CTOR

        // GET api/core/status
        /// <summary>
        /// Gateway status
        /// </summary>
        /// <returns></returns>        
        [HttpHead()]
        [HttpGet()]
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
        [HttpGet()]
        [CacheTangleResponse(
            LifeSpan = 86000,
            StatusCode = (int)HttpStatusCode.OK)
            ]
        [Produces("application/javascript")]
        [ProducesResponseType(typeof(NodeTree), (int)HttpStatusCode.OK)]        
        public IActionResult ApiMapCalls()
        {
            // var TargetURL = Request.Scheme + "://" + Request.Host.ToString() + Program.SwaggerJSONFile();
            var basePath = _conf.GetValue<string>("DefaultPublicFacingHttpUrl", Request.Scheme + "://" + Request.Host.ToString());
            var TargetURL = basePath + Program.SwaggerJsonFile(); 

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


#if DEBUG
        [HttpGet()]
        public IActionResult PerformSelfTest()
        {
            // var TargetURL = Request.Scheme + "://" + Request.Host.ToString() + Program.SwaggerJSONFile();
            var basePath = _conf.GetValue<string>("DefaultPublicFacingHttpUrl", Request.Scheme + "://" + Request.Host.ToString());
            var TargetURL = basePath + Program.SwaggerJsonFile();
            var Address = "CYJV9DRIE9NCQJYLOYOJOGKQGOOELTWXVWUYGQSWCNODHJAHACADUAAHQ9ODUICCESOIVZABA9LTMM9RW";
            var BundleHash = "TXGCKHFQPOYLN9BZNLXEKAGVGSRGHHUJUQTRYKJLAHTHUBEBQSBUXGOASFOI9KTUIURHODX9HZFSIHANX";
            var TXHash = "JYSCFGQDBICFTJKQTRSPOJNCJ9IBAONHTSAQVVYIZVHLVEKGCLLWA9NPQWEEUOBGSHOXKDQSCIVTZ9999";

            _logger.LogInformation("Trying to get API definition from {TargetURL}", TargetURL);

            var client = new RestSharp.RestClient(TargetURL) { Timeout = 2000 };
            var resp = client.Execute(new RestSharp.RestRequest(TargetURL, RestSharp.Method.GET));
            
            if (resp.IsSuccessful && resp.StatusCode == HttpStatusCode.OK)
            {
                // Getting list of all get procedures
                var Source = JsonConvert.DeserializeObject<JObject>(resp.Content);
                var TestClient = new RestSharp.RestClient(basePath) { Timeout = 10000 };

                var TestResults = new List<SelfTestResult>();
                var ApiCalls = (from item in Source["paths"]
                               from v in item.Values()
                               where (v as JProperty).Name == "get"
                               select (item as JProperty).Name).ToList();

                foreach (var item in ApiCalls) // let's cycle thru all API paths
                {
                    var ApiCall = item;
                    if (ApiCall.Contains("bundle", StringComparison.InvariantCultureIgnoreCase))
                    {
                        ApiCall = ApiCall.Replace("{hash}", BundleHash, StringComparison.InvariantCultureIgnoreCase);
                    }
                    if (ApiCall.Contains("transaction", StringComparison.InvariantCultureIgnoreCase))
                    {
                        ApiCall = ApiCall.Replace("{hash}", TXHash, StringComparison.InvariantCultureIgnoreCase);
                    }
                    ApiCall = ApiCall.Replace("{address}", Address, StringComparison.InvariantCultureIgnoreCase);
                    ApiCall = basePath + ApiCall;

                    if (!ApiCall.Contains(nameof(PerformSelfTest),StringComparison.InvariantCultureIgnoreCase))
                    {
                        var result = TestClient.Execute(new RestSharp.RestRequest(ApiCall, RestSharp.Method.GET));
                        TestResults.Add(new SelfTestResult() { ApiCall = ApiCall, StatusCode = (int)result.StatusCode });
                        if ((int)result.StatusCode >= 500)
                        {
                            break;
                        }
                    }                    
                }
                return Json(TestResults); // Format the output
            }
            return StatusCode(500);
        }
#endif
    }
}
