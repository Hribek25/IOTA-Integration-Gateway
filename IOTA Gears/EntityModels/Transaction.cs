using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTA_Gears.EntityModels
{
    public class TransactionContainer
    {
        public Tangle.Net.Entity.Transaction Transaction { get; set; }
        public bool? IsConfirmed { get; set; } = null;

        public TransactionContainer()
        {
            // default ctor because of deserialization
        }

        public TransactionContainer(Tangle.Net.Entity.Transaction transaction)
        {
            Transaction = transaction;
        }

        public TransactionContainer(Tangle.Net.Entity.TransactionTrytes trytes)
        {
            Transaction = Tangle.Net.Entity.Transaction.FromTrytes(trytes);
        }
    }
}
