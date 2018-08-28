using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace IOTA_Gears
{
    public class CommonHelpers
    {
        public static bool IsValidAddress(string address) =>
            Regex.IsMatch(address, @"^(([A-Z9]{90})|([A-Z9]{81}))$");
    }
}
