using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.EntityModels
{
    public enum StatusEnum
    {
        OK,
        Failed
    }
    
    public class GatewayStatus
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public StatusEnum Status { get; set; }

        public string Version { get; set; }
    }

}
