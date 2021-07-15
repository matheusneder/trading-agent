using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    public enum Stage
    {
        JustRegistered = 1,
        CreatingBuyOrder = 2,
        BuyOrderCreated = 3,
        BuyOrderFilled = 4,
        ParametersCalculated = 5,
        CreatingSellOrder = 6,
        SellOrderCreated = 7,
        SellOrderFilled = 8,
        RollbackCancellingOcoOrder = 9,
        RollbackCancelOcoOrderExecuted = 10,
        RollbackCancelOcoOrderCancelled = 11,
        CompletedAndNotInitialized = 99
    }
}
