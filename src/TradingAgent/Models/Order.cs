using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    public class Order
    {
        public int TradingId { get; set; }
        public OrderKind OrderKind { get; set; }
        public string HoldAsset { get; set; }
        public string TradeAsset { get; set; }
        public decimal ExecutedQty { get; set; }
        public decimal CummulativeQuoteQty { get; set; }
        public decimal Price => ExecutedQty > 0m ? CummulativeQuoteQty / ExecutedQty : 0m;
        public string Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
