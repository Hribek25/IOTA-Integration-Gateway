using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tangle.Net.Entity;

namespace IOTAGears
{
    public class TXHashComparer : IEqualityComparer<Hash>
    {
        public bool Equals(Hash x, Hash y)
        {
            return x.Value==y.Value;
        }

        public int GetHashCode(Hash obj)
        {
            return obj.Value.GetHashCode(StringComparison.InvariantCulture);
        }
    }
}
