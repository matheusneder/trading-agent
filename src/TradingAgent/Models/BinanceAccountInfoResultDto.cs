using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    public class BinanceAccountInfoResultDto
    {
        public int makerCommission { get; set; }
        public int takerCommission { get; set; }
        public int buyerCommission { get; set; }
        public int sellerCommission { get; set; }
        public bool canTrade { get; set; }
        public bool canWithdraw { get; set; }
        public bool canDeposit { get; set; }
        public long updateTime { get; set; }
        public string accountType { get; set; }
        public List<BalanceDto> balances { get; set; }
        public List<string> permissions { get; set; }

        public class BalanceDto
        {
            public string asset { get; set; }
            public string free { get; set; }
            public string locked { get; set; }
        }
    }
}
