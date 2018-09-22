using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.EntityModels
{
    public class TaskEntry
    {        
        public TaskEntryInput Input { get; set; }
        public string Task { get; set; }
        public long Timestamp { get; set; }
        public string GlobalId { get; set; }
    }

    public class PipelineStatus
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public StatusDetail Status { get; set; }

        public string GlobalId { get; set; }
        public Int64 NumberOfRequests { get; set; } = -1;
    }

    public enum StatusDetail
    {
        TaskWasAddedToPipeline,
        TooManyRequests
    }

    public class TaskEntryInput
    {
        public string Address { get; set; }
        public string Message { get; set; }
        public string Tag { get; set; }
    }

}
