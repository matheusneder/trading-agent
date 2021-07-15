using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    public class BinanceQueryOcoResultDto
    {
        public int orderListId { get; set; }
        public string contingencyType { get; set; }
        public string listStatusType { get; set; }
        public string listOrderStatus { get; set; }
        public string listClientOrderId { get; set; }
        public long transactionTime { get; set; }
        public string symbol { get; set; }
        public List<OrderDto> orders { get; set; }

        public class OrderDto
        {
            public string symbol { get; set; }
            public int orderId { get; set; }
            public string clientOrderId { get; set; }
        }
    }
}
