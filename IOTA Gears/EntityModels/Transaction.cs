using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.EntityModels
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

    public class TransactionContainer : IEqualityComparer<TransactionContainer>
    {
        public Tangle.Net.Entity.Transaction Transaction { get; set; }
        public bool? IsConfirmed { get; set; } = null;
        public string DecodedMessage { get; set; } = null;
        public long Timestamp { get; set; } = 0;

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
                if (DecodedMessage != null && !TransactionContainer.IsPrintableMessage(DecodedMessage))
                {
                    DecodedMessage = null;
                }
            }
            catch (Exception)
            {
                this.DecodedMessage = null;
            }

            this.TransactionType = Transaction.Value != 0 ? TransactionType.ValueTransaction : TransactionType.NonValueTransaction;
            this.Timestamp = Transaction.Timestamp;
        }

        public TransactionContainer(Tangle.Net.Entity.TransactionTrytes trytes) : this(Tangle.Net.Entity.Transaction.FromTrytes(trytes))
        {
        }
        
        private static bool IsPrintableMessage(string message)
        {
            byte[] encodedBytes = System.Text.Encoding.UTF8.GetBytes(message);

            bool printable = true;
            for (int ctr = 0; ctr < encodedBytes.Length; ctr++)
            {
                if (encodedBytes[ctr]<32)
                {
                    printable = false;
                    break;
                }                 
            }

            return printable;
        }

        public bool Equals(TransactionContainer x, TransactionContainer y)
        {
            return x.Transaction.Hash.Value==y.Transaction.Hash.Value;
        }

        public int GetHashCode(TransactionContainer obj)
        {
            return obj.Transaction.Hash.Value.GetHashCode(StringComparison.InvariantCulture);
        }                
    }
}
