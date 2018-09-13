using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace IOTAGears
{
    public static class CommonHelpers
    {
        public static bool IsValidAddress(string address) =>
            Regex.IsMatch(address, @"^(([A-Z9]{90})|([A-Z9]{81}))$");

        public static bool IsValidHash(string hash) =>
            Regex.IsMatch(hash, @"^([A-Z9]{81})$");
        
    }
}
