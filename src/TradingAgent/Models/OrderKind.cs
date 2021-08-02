using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    public enum OrderKind
    {
        BuyMarketOrder = 1,
        SellOcoOrder = 2,
        SellOcoLimitOrder = 3,
        SellOcoStopOrder = 4,
        SellOcoRollbackOrder = 5,
        SellOcoLimitRollbackOrder = 6,
        SellOcoStopRollbackOrder = 7  
    }
}
