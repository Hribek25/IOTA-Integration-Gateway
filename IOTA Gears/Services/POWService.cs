using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.Services
{
    public class POWService : Tangle.Net.ProofOfWork.Service.PoWSrvService
    {
        public POWService(string nodeurl, string apikey = null) : base(new RestSharp.RestClient(nodeurl), apikey)
        {

        }
        
    }
}
