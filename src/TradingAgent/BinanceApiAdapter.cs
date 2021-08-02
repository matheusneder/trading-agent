using Refit;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public class BinanceApiAdapter : BinanceApiAdapterBase
    {
        private readonly IBinancePrivateApiClient binancePrivateApiClient;
        private readonly IBinancePublicApiClient binancePublicApiClient;

        public BinanceApiAdapter(IBinancePrivateApiClient binancePrivateApiClient, IBinancePublicApiClient binancePublicApiClient)
        {
            this.binancePrivateApiClient = binancePrivateApiClient ?? throw new ArgumentNullException(nameof(binancePrivateApiClient));
            this.binancePublicApiClient = binancePublicApiClient ?? throw new ArgumentNullException(nameof(binancePublicApiClient));
        }

        public async Task<Account> GetAccountInformationAsync()
        {
            try
            {
                var dto = await binancePrivateApiClient.GetAccountInformationAsync();

                return new Account()
                {
                    Balances = dto.balances.Select(b => new Balance()
                    {
                        Asset = b.asset,
                        Free = decimal.Parse(b.free, CultureInfo.InvariantCulture.NumberFormat),
                        Locked = decimal.Parse(b.locked, CultureInfo.InvariantCulture.NumberFormat)
                    })
                };
            }
            catch(ApiException e) 
            {
                await HandleErrorsAsync(e);

                throw;
            }
        }

        public async Task<decimal> GetBalanceAsync(string asset)
        {
            var accInfo = await GetAccountInformationAsync();

            return accInfo.Balances.SingleOrDefault(b => b.Asset == asset)?.Free ?? 0;
        }

        private string GenerateClientOrderId(int tradingId, OrderKind orderKind)
        {
            

            string origClientOrderId;

            switch (orderKind)
            {
                case OrderKind.BuyMarketOrder:
                    origClientOrderId = $"BuyMarketOrder-{tradingId}";
                    break;
                case OrderKind.SellOcoOrder:
                    origClientOrderId = $"SellOcoOrder-{tradingId}";
                    break;
                case OrderKind.SellOcoRollbackOrder:
                    origClientOrderId = $"SellOcoRollbackOrder-{tradingId}";
                    break;
                case OrderKind.SellOcoStopLimitOrder:
                    origClientOrderId = $"SellOcoStopLimitOrder-{tradingId}";
                    break;
                case OrderKind.SellOcoStopLimitRollbackOrder:
                    origClientOrderId = $"SellOcoStopLimitRollbackOrder-{tradingId}";
                    break;
                case OrderKind.SellOcoLimitOrder:
                    origClientOrderId = $"SellOcoLimitOrder-{tradingId}";
                    break;
                case OrderKind.SellOcoLimitRollbackOrder:
                    origClientOrderId = $"SellOcoLimitRollbackOrder-{tradingId}";
                    break;
                default:
                    origClientOrderId = $"Unmapped-{tradingId}";
                    break;
            }

            return origClientOrderId;
        }

        public async Task<Order> GetOrderAsync(int tradingId, string holdAsset, string tradeAsset, OrderKind orderKind, string sellOrderBinanceIdSuffix = null)
        {
            var origClientOrderId = GenerateClientOrderId(tradingId, orderKind);

            if (sellOrderBinanceIdSuffix != null) 
            {
                switch (orderKind)
                {
                    case OrderKind.SellOcoStopLimitRollbackOrder:
                    case OrderKind.SellOcoStopLimitOrder:
                        origClientOrderId = $"TR-{tradingId}-STOP-{sellOrderBinanceIdSuffix}";
                        break;
                    case OrderKind.SellOcoLimitRollbackOrder:
                    case OrderKind.SellOcoLimitOrder:
                        origClientOrderId = $"TR-{tradingId}-LIMIT-{sellOrderBinanceIdSuffix}";
                        break;
                }
            }

            try
            {
                var dto = await binancePrivateApiClient.QueryOrderAsync($"{tradeAsset}{holdAsset}", origClientOrderId);

                return new Order() 
                { 
                    TradingId = tradingId,
                    OrderKind = orderKind,
                    HoldAsset = holdAsset,
                    TradeAsset = tradeAsset,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.time),
                    UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.updateTime),
                    Status = dto.status,
                    CummulativeQuoteQty = decimal.Parse(dto.cummulativeQuoteQty, CultureInfo.InvariantCulture.NumberFormat),
                    ExecutedQty = decimal.Parse(dto.executedQty, CultureInfo.InvariantCulture.NumberFormat)
                };
            }
            catch(ApiException e)
            {
                bool orderNotFound = false;

                await HandleErrorsAsync(e, code => orderNotFound = code == -2013);

                if (orderNotFound)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task CreateBuyOrderAsync(int tradingId, string holdAsset, string tradeAsset, decimal buyOrderQuoteQty)
        {
            try
            {
                await binancePrivateApiClient
                    .NewOrderAsync(
                        symbol: $"{tradeAsset}{holdAsset}",
                        side: "BUY",
                        type: "MARKET",
                        quoteOrderQty: buyOrderQuoteQty.ToString("0.####", CultureInfo.InvariantCulture), // TODO: read precision from specific pair attribute (GET from /api/v3/exchangeInfo)
                        newClientOrderId: GenerateClientOrderId(tradingId, OrderKind.BuyMarketOrder));
            }
            catch(ApiException e)
            {
                await HandleErrorsAsync(e);

                throw;
            }
        }

        public async Task CreateSellOrderAsync(int tradingId, string holdAsset, string tradeAsset, decimal tradeAssetQty, decimal sellPrice, decimal sellStopLimitPrice, string sellOrderBinanceIdSuffix)
        {
           
            try
            {
                await binancePrivateApiClient
                    .NewOcoAsync(
                        symbol: $"{tradeAsset}{holdAsset}",
                        listClientOrderId: $"TR-{tradingId}-LIST-{sellOrderBinanceIdSuffix}",
                        side: "SELL",
                        quantity: tradeAssetQty.ToString("0.####", CultureInfo.InvariantCulture), // TODO: read precision from specific pair attribute (GET from /api/v3/exchangeInfo)
                        limitClientOrderId: $"TR-{tradingId}-LIMIT-{sellOrderBinanceIdSuffix}", 
                        price: sellPrice.ToString("0.####", CultureInfo.InvariantCulture), // TODO: read precision from specific pair attribute (GET from /api/v3/exchangeInfo)
                        stopClientOrderId: $"TR-{tradingId}-STOP-{sellOrderBinanceIdSuffix}",
                        stopPrice: sellStopLimitPrice.ToString("0.####", CultureInfo.InvariantCulture), // TODO: read precision from specific pair attribute (GET from /api/v3/exchangeInfo)
                        stopLimitPrice: sellStopLimitPrice.ToString("0.####", CultureInfo.InvariantCulture), // TODO: read precision from specific pair attribute (GET from /api/v3/exchangeInfo)
                        stopLimitTimeInForce: "GTC"
                    );
            }
            catch(ApiException e)
            {
                await HandleErrorsAsync(e);

                throw;
            }
        }

        public async Task CancelOcoOrderAsync(int tradingId, string holdAsset, string tradeAsset, string sellOrderBinanceIdSuffix)
        {
            try
            {
                await binancePrivateApiClient.CancelOco(
                    symbol: $"{tradeAsset}{holdAsset}",
                    listClientOrderId: $"TR-{tradingId}-LIST-{sellOrderBinanceIdSuffix}");
            }
            catch(ApiException e)
            {
                await HandleErrorsAsync(e);

                throw;
            }
        }

        public async Task<string> GetOcoOrderStatusAsync(int tradingId, string sellOrderBinanceIdSuffix)
        {
            try
            {
                var dto = await binancePrivateApiClient.QueryOcoAsync($"TR-{tradingId}-LIST-{sellOrderBinanceIdSuffix}");

                return dto.listOrderStatus;
            }
            catch(ApiException e)
            {
                bool orderNotFound = false;

                await HandleErrorsAsync(e, code => orderNotFound = code == -2018);

                if (orderNotFound)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<decimal> GetCurrentPrice(string holdAsset, string tradeAsset)
        {
            try
            {
                var dto = await binancePublicApiClient.SymbolPriceTicker($"{tradeAsset}{holdAsset}");

                return decimal.Parse(dto.price, CultureInfo.InvariantCulture.NumberFormat);
            }
            catch(ApiException e)
            {
                await HandleErrorsAsync(e);

                throw;
            }
        }
    }
}
