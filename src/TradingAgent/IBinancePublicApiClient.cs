using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public interface IBinancePublicApiClient
    {

        [Get("/api/v3/ticker/price")]
        Task<SymbolPriceTickerResultDto> SymbolPriceTicker(string symbol);
    }
}
