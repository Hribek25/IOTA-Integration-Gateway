using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.EntityModels
{
    public class TxHashSetCollection : HashSet<TransactionContainer>
    {
        public bool CompleteSet { get; set; } = true;

        public TxHashSetCollection() : base()
        {
        }
    }
}
