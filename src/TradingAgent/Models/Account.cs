using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    public class Account
    {
        public IEnumerable<Balance> Balances { get; set; }
    }
}
