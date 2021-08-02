using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public interface IBinancePrivateApiClient
    {
        [Get("/api/v3/account")]
        Task<BinanceAccountInfoResultDto> GetAccountInformationAsync();

        [Get("/api/v3/order")]
        Task<BinanceQueryOrderResultDto> QueryOrderAsync(string symbol, string origClientOrderId);

        [Get("/api/v3/orderList")]
        Task<BinanceQueryOcoResultDto> QueryOcoAsync(string origClientOrderId);

        [Post("/api/v3/order")]
        Task NewOrderAsync(string symbol, string side, string type, string quoteOrderQty, string newClientOrderId);

        [Post("/api/v3/order/oco")]
        Task NewOcoAsync(string symbol, string listClientOrderId, string side, string quantity, string limitClientOrderId, string price, string stopClientOrderId, string stopPrice, string stopLimitPrice = null, string stopLimitTimeInForce = null);

        [Delete("/api/v3/orderList")]
        Task CancelOco(string symbol, string listClientOrderId);
    }
}
