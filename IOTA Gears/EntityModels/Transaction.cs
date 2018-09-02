using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace IOTA_Gears.EntityModels
{    
    public enum TransactionFilter
    {
        ConfirmedOnly,
        All        
    }

    public enum TransactionType
    {
        ValueTransaction,
        NonValueTransaction
    }

    public class TransactionContainer
    {
        public Tangle.Net.Entity.Transaction Transaction { get; set; }
        public bool? IsConfirmed { get; set; } = null;
        public string DecodedMessage { get; set; } = null;

        [JsonConverter(typeof(StringEnumConverter))]
        public TransactionType TransactionType { get; set; }

        public TransactionContainer()
        {
            // default ctor because of deserialization
        }

        public TransactionContainer(Tangle.Net.Entity.Transaction transaction)
        {
            Transaction = transaction;

            try
            {
                this.DecodedMessage = !Transaction.Fragment.IsEmpty ? Transaction.Fragment.ToUtf8String() : null;
            }
            catch (Exception)
            {

                this.DecodedMessage = null;
            }
            
            this.TransactionType = Transaction.Value != 0 ? TransactionType.ValueTransaction : TransactionType.NonValueTransaction;
        }

        public TransactionContainer(Tangle.Net.Entity.TransactionTrytes trytes) : this(Tangle.Net.Entity.Transaction.FromTrytes(trytes))
        {

        }        
    }
}
