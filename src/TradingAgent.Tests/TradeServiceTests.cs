using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Server;
using Moq;
using Newtonsoft.Json;
using Refit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TradingAgent.Models;
using Xunit;

namespace TradingAgent.Tests
{
    public class TradeServiceTests
    {
        private readonly Mock<IBinancePrivateApiClient> binancePrivateApiClientMock = new Mock<IBinancePrivateApiClient>();
        private readonly Mock<IBinancePublicApiClient> binancePublicApiCLientMock = new Mock<IBinancePublicApiClient>();
        private readonly DbAdapter dbAdapter;
        private readonly BinanceApiAdapter binanceApiAdapter;
        private readonly AppConfig appConfig = AppConfig.ReadFromFile();
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;

        public TradeServiceTests()
        {
            loggerFactory = LoggerFactory.Create(config =>
            {
                config.AddDebug();
            });

            logger = loggerFactory.CreateLogger<TradeServiceTests>();

            binancePrivateApiClientMock.Setup(m => m.GetAccountInformationAsync())
                .ReturnsAsync(new BinanceAccountInfoResultDto()
            {
                balances = new List<BinanceAccountInfoResultDto.BalanceDto>()
                {
                    new BinanceAccountInfoResultDto.BalanceDto()
                    {
                        asset = "BNB",
                        free = "200.000",
                        locked = "0"
                    }
                }
            });

            binancePrivateApiClientMock.Setup(m => m.QueryOcoAsync(It.IsAny<string>()))
                .Callback(() => { })
                .ReturnsAsync(new BinanceQueryOcoResultDto()
                {
                    listOrderStatus = "ALL_DONE"
                });

            // Order de compra
            binancePrivateApiClientMock.Setup(m => m.QueryOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BinanceQueryOrderResultDto()
                {
                    executedQty = "220",
                    cummulativeQuoteQty = "200",
                    status = "FILLED",
                    time = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    updateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });

            binancePrivateApiClientMock.Setup(m => m.QueryOrderAsync(It.IsAny<string>(),
                It.IsRegex("^TR-[0-9]+-STOP-[a-z0-9]+$")))
                .ReturnsAsync(new BinanceQueryOrderResultDto()
                {
                    executedQty = "0.00000000",
                    cummulativeQuoteQty = "0.00000000",
                    status = "EXPIRED",
                    time = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    updateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                });

            binancePublicApiCLientMock.Setup(m => m.SymbolPriceTicker(It.IsAny<string>()))
                .ReturnsAsync(new SymbolPriceTickerResultDto()
                {
                    symbol = "LTC",
                    price = "0.909"
                });

            dbAdapter = new DbAdapter(AppSecrets.ReadFromFile());
            binanceApiAdapter = new BinanceApiAdapter(binancePrivateApiClientMock.Object, binancePublicApiCLientMock.Object);
        }

        private TradeService CreateTradeService()
        {
            return new TradeService(loggerFactory, dbAdapter, binanceApiAdapter, appConfig)
            {
                SkipDelays = true
            };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteFlow_Success(bool queryOcoOnce)
        {
            logger.LogInformation($"{nameof(ExecuteFlow_Success)}(queryOcoOnce: {queryOcoOnce})");

            var holdAsset = appConfig.HoldAsset;

            SetupQueryOco(queryOcoOnce);

            var tradeService = CreateTradeService();
            await dbAdapter.InactivateAllAsync(holdAsset);
            var tradingId = await tradeService.Step1CheckConditionsThenRegisterANewTradeAsync();
            await Task.Delay(10);

            var trading = await dbAdapter.GetTradingAsync(tradingId);

            Assert.False(trading.IsRollback);
            Assert.Equal(Stage.SellOrderFilled, trading.Stage);
            Assert.False(trading.Active);
            Assert.Equal(OrderKind.SellOcoLimitOrder, trading.SellOrderKind);
        }

        [Fact]
        public async Task BinanceSignatureOrTimestampMaxAttempts_Error()
        {
            var holdAsset = appConfig.HoldAsset;

            SetupQueryOco(false);

            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri("https://dummy.com")
            };

            var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = httpRequestMessage,
                Content = new StringContent(JsonConvert.SerializeObject(new BinanceErrorDto()
                {
                    code = -1021,
                    msg = "Timestamp error"
                }), Encoding.UTF8, "application/json")
            };

            binancePrivateApiClientMock.Setup(m => m.NewOrderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(await ApiException.Create(
                    httpRequestMessage, 
                    HttpMethod.Post, 
                    httpResponseMessage,
                    new RefitSettings()));

            var tradeService = CreateTradeService();
            await dbAdapter.InactivateAllAsync(holdAsset);

            await Assert.ThrowsAsync<BinanceSignatureOrTimestampMaxAttemptsException>(async () => 
                await tradeService.Step1CheckConditionsThenRegisterANewTradeAsync());
        }

        [Fact]
        public async Task ExecuteRollback_Success()
        {
            var holdAsset = appConfig.HoldAsset;

            SetupQueryOco(queryOcoOnce: false);

            decimal increment = -0.02m;
            decimal price = 0.909m + increment * -1;

            var symbolPriceTickerResult = new SymbolPriceTickerResultDto()
            {
                symbol = "LTC",
                price = price.ToString("G", CultureInfo.InvariantCulture)
            };

            binancePublicApiCLientMock.Setup(m => m.SymbolPriceTicker(It.IsAny<string>()))
                .Callback(() =>
                {
                    var newPrice = price + increment;
                    
                    if(newPrice > 0)
                    {
                        price = newPrice;
                    }
                    else
                    {
                        price = 0m;
                    }

                    symbolPriceTickerResult.price = price.ToString("G", CultureInfo.InvariantCulture);
                })
                .ReturnsAsync(symbolPriceTickerResult);

            var tradeService = CreateTradeService();
            await dbAdapter.InactivateAllAsync(holdAsset);
            int tradingId = await tradeService.Step1CheckConditionsThenRegisterANewTradeAsync();
            await Task.Delay(10);

            var trading = await dbAdapter.GetTradingAsync(tradingId);

            Assert.True(trading.IsRollback);
            Assert.Equal(Stage.SellOrderFilled, trading.Stage);
            Assert.False(trading.Active);
            Assert.Equal(OrderKind.SellOcoLimitRollbackOrder, trading.SellOrderKind);
        }

        [Fact]
        public async Task ExecuteUpgrade_Success()
        {
            var holdAsset = appConfig.HoldAsset;

            SetupQueryOco(queryOcoOnce: false);

            decimal increment = 0.001m;
            decimal price = 0.909m + increment;

            var symbolPriceTickerResult = new SymbolPriceTickerResultDto()
            {
                symbol = "LTC",
                price = price.ToString("G", CultureInfo.InvariantCulture)
            };

            binancePublicApiCLientMock.Setup(m => m.SymbolPriceTicker(It.IsAny<string>()))
                .Callback(() =>
                {
                    var newPrice = price + increment;

                    if (newPrice > 0)
                    {
                        price = newPrice;
                    }
                    else
                    {
                        price = 0m;
                    }

                    symbolPriceTickerResult.price = price.ToString("G", CultureInfo.InvariantCulture);
                })
                .ReturnsAsync(symbolPriceTickerResult);

            var tradeService = CreateTradeService();
            await dbAdapter.InactivateAllAsync(holdAsset);
            int tradingId = await tradeService.Step1CheckConditionsThenRegisterANewTradeAsync();
            await Task.Delay(10);

            var trading = await dbAdapter.GetTradingAsync(tradingId);

            Assert.False(trading.IsRollback);
            Assert.Equal(Stage.SellOrderFilled, trading.Stage);
            Assert.False(trading.Active);
            Assert.True(trading.UpgradeCount > 0);
            Assert.Equal(OrderKind.SellOcoLimitOrder, trading.SellOrderKind);
        }

        [Fact]
        public async Task ExecuteUpgradeThenRollback_Success()
        {
            var holdAsset = appConfig.HoldAsset;

            SetupQueryOco(queryOcoOnce: false);

            decimal increment = 0.05m;
            decimal price = 0.909m + increment;

            var symbolPriceTickerResult = new SymbolPriceTickerResultDto()
            {
                symbol = "LTC",
                price = price.ToString("G", CultureInfo.InvariantCulture)
            };

            binancePublicApiCLientMock.Setup(m => m.SymbolPriceTicker(It.IsAny<string>()))
                .Callback(() =>
                {
                    var newPrice = price + increment;

                    if (newPrice > 0)
                    {
                        price = newPrice;
                        increment = -0.50m;
                    }
                    else
                    {
                        price = 0m;
                    }

                    symbolPriceTickerResult.price = price.ToString("G", CultureInfo.InvariantCulture);
                })
                .ReturnsAsync(symbolPriceTickerResult);

            var tradeService = CreateTradeService();
            await dbAdapter.InactivateAllAsync(holdAsset);
            int tradingId = await tradeService.Step1CheckConditionsThenRegisterANewTradeAsync();
            await Task.Delay(10);

            var trading = await dbAdapter.GetTradingAsync(tradingId);

            Assert.True(trading.IsRollback);
            Assert.Equal(Stage.SellOrderFilled, trading.Stage);
            Assert.False(trading.Active);
            Assert.True(trading.UpgradeCount > 0);
            Assert.Equal(OrderKind.SellOcoLimitRollbackOrder, trading.SellOrderKind);
        }

        private void SetupQueryOco(bool queryOcoOnce)
        {
            var queryOcoResult = new BinanceQueryOcoResultDto()
            {
                listOrderStatus = "ALL_DONE"
            };

            if (!queryOcoOnce)
            {
                queryOcoResult.listOrderStatus = "EXECUTING";
            }

            int count = 0;

            binancePrivateApiClientMock.Setup(m => m.QueryOcoAsync(It.IsAny<string>()))
                .Callback(() =>
                {
                    if (count >= 3)
                    {
                        queryOcoResult.listOrderStatus = "ALL_DONE";
                        count = 0;
                    }
                    else
                    {
                        if (!queryOcoOnce)
                        {
                            queryOcoResult.listOrderStatus = "EXECUTING";
                        }
                    }

                    count++;
                })
                .ReturnsAsync(queryOcoResult);
            
        }
    }
}
