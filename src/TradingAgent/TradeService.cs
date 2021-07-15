using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public class TradeService
    {
        private readonly ILogger logger;
        private readonly DbAdapter dbAdapter;
        private readonly BinanceApiAdapter binanceApiAdapter;
        private readonly AppConfig appConfig;
        private const int WatchPriceInterval = 2000;
        private const int WatchOrderInterval = 30000;
        private static readonly int WatchPriceMaxIterations = Convert.ToInt32(Math.Round((double)WatchOrderInterval / (double)WatchPriceInterval));

        public bool SkipDelays { get; set; } = false; // for test purposes only

        private Task DelayAsync(int milliseconds) => SkipDelays ? Task.CompletedTask : Task.Delay(milliseconds);

        public TradeService(ILoggerFactory loggerFactory, DbAdapter dbAdapter, BinanceApiAdapter binanceApiAdapter, AppConfig appConfig)
        {
            logger = loggerFactory?.CreateLogger<TradeService>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.dbAdapter = dbAdapter ?? throw new ArgumentNullException(nameof(dbAdapter));
            this.binanceApiAdapter = binanceApiAdapter ?? throw new ArgumentNullException(nameof(binanceApiAdapter));
            this.appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        }

        public async Task<int> Step1CheckConditionsThenRegisterANewTradeAsync()
        {
            int tradingId = -1;
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;

            var stopLossPercent = appConfig.StopLossPercent;

            if (await dbAdapter.AnyActiveTradeAsync(holdAsset))
            {
                logger.LogInformation("Seems there is an incomplete trade running. Skiping for now...");
            }
            else
            {
                var holdAssetBalance = await binanceApiAdapter.GetBalanceAsync(holdAsset);
                logger.LogInformation("Current {Asset} balance: {Balance}", holdAsset, holdAssetBalance);
                var stopTradingThreshold = await dbAdapter.GetStopThresholdAsync(holdAsset);
                var sellStopQuoteQtyOverTotalBalance = MinusPercentage(holdAssetBalance, stopLossPercent);

                if (sellStopQuoteQtyOverTotalBalance >= stopTradingThreshold)
                {
                    logger.LogInformation(
                        $"sellStopQuoteQty >= stopTradingThreshold ({sellStopQuoteQtyOverTotalBalance} >= {stopTradingThreshold}); going to trade...");

                    decimal buyOrderQuoteQty = MinusPercentage(holdAssetBalance, 0.5m);

                    logger.LogInformation("BuyOrderQuoteQty: {BuyOrderQuoteQty}", buyOrderQuoteQty);

                    tradingId = await dbAdapter.InsertNewOperationAsync(holdAsset, tradeAsset, buyOrderQuoteQty);

                    await Step2CreateBuyOrderAsync();
                }
                else
                {
                    logger.LogWarning(
                        $"sellStopQuoteQty < stopTradingThreshold ({sellStopQuoteQtyOverTotalBalance} < {stopTradingThreshold}); PERMANENTLY SKIPING trading ...");
                }
            }

            return tradingId;
        }

        public async Task Step2CreateBuyOrderAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.JustRegistered);

            if (activeTrading != null)
            {
                if (activeTrading.CreatedAt.AddSeconds(20) < DateTimeOffset.Now)
                {
                    logger.LogWarning("Skiping create buy order due old order register (CreateAt). Updating it as completed.", activeTrading.CreatedAt);

                    await dbAdapter.UpdateTradeCompletedAndNotInitializedStageAsync(activeTrading.Id, "Skiping create buy order due old order register");
                }
                else
                {
                    await dbAdapter.UpdateTradingCreatingBuyOrderStageAsync(activeTrading.Id);
                    
                    logger.LogInformation($"Trading #{{TradingId}}. Creating buy order!");

                    await BinanceSignatureOrTimestampErrorRetrierHelper(async () => 
                        await binanceApiAdapter.CreateBuyOrderAsync(activeTrading.Id, holdAsset, tradeAsset, activeTrading.BuyOrderQuoteQty));

                    await Step3UpdateOrderCreatedStageAsync();
                }
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step2CreateBuyOrderAsync)}, there is no active order on Stage {Stage.JustRegistered}.");
            }
        }

        private async Task BinanceSignatureOrTimestampErrorRetrierHelper(Func<Task> action)
        {
            bool createBuyOrderExecuted = false;
            int createBuyOrderAttempts = 0;
            int nextRetryDelay = 1000;
            int maxAttempts = 15;

            do
            {
                try
                {
                    await action.Invoke();
                    createBuyOrderExecuted = true;
                }
                catch (Exception e) when (e is BinanceSignatureException || e is BinanceTimestampException)
                {
                    logger.LogWarning(e, "BinanceSignatureException || BinanceTimestampException occurred.");

                    if (createBuyOrderAttempts > maxAttempts)
                    {
                        throw new BinanceSignatureOrTimestampMaxAttemptsException(e);
                    }

                    logger.LogInformation("Retring in {NextRetryDelay} ms", nextRetryDelay);
                    nextRetryDelay += 100;

                    await DelayAsync(nextRetryDelay);
                }

                createBuyOrderAttempts++;
            } while (!createBuyOrderExecuted);
        }
   

        public async Task Step3UpdateOrderCreatedStageAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.CreatingBuyOrder);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateOrderCreatedStageAsync(activeTrading.Id);
                logger.LogInformation($"Trading #{{TradingId}}. Buy order created!");

                await Step4RetriveBuyOrderDataAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step3UpdateOrderCreatedStageAsync)}, there is no active order on Stage {Stage.CreatingBuyOrder}.");
            }
        }

        public async Task Step4RetriveBuyOrderDataAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.BuyOrderCreated);

            if (activeTrading != null)
            {
                Order order;

                bool mustRetriveAgain = false;

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                do
                {
                    var delay = DelayAsync(2000);
                    order = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, holdAsset, tradeAsset, OrderKind.BuyMarketOrder);
                    mustRetriveAgain = order == null || order.Status != "FILLED";

                    switch (order?.Status)
                    {
                        case "CANCELED":
                        case "REJECTED":
                        case "EXPIRED":
                            throw new InvalidOperationException($"Unexpected buy order status {order?.Status}");
                    }

                    if(stopwatch.ElapsedMilliseconds > 35000)
                    {
                        throw new InvalidOperationException($"Taking too long to get buy order filled. Order status: {order?.Status}");
                    }

                    if (mustRetriveAgain)
                    {
                        await delay;
                    }

                } while (mustRetriveAgain);

                await dbAdapter.UpdateBuyOrderFilledStageAsync(order);

                logger.LogInformation("Buy order executed!");

                await Step5CalcSellOrderParamsAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step4RetriveBuyOrderDataAsync)}, there is no active order on Stage {Stage.BuyOrderCreated}.");
            }
        }

        private decimal PlusPercentage(decimal value, decimal percentage) => value * (1 + percentage / 100);
        private decimal MinusPercentage(decimal value, decimal percentage) => value - (value * percentage / 100);

        public async Task Step5CalcSellOrderParamsAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var targetProfitPerTradePercent = appConfig.TargetProfitPerTradePercent;
            var stopLossPercent = appConfig.StopLossPercent;
            var rollbackPricePercent = appConfig.RollbackPricePercent;

            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.BuyOrderFilled);


            if (activeTrading != null)
            {
                decimal buyPrice = activeTrading.BuyPrice ?? throw new InvalidOperationException($"{nameof(Trading.BuyPrice)} is null");
                decimal sellPrice = PlusPercentage(buyPrice, targetProfitPerTradePercent);
                decimal sellStopLimitPrice = MinusPercentage(buyPrice, stopLossPercent);
                decimal rollbackPrice = MinusPercentage(buyPrice, rollbackPricePercent);

                await dbAdapter.UpdateSellOrderParametersCalculatedStageAsync(activeTrading.Id, sellPrice, sellStopLimitPrice, rollbackPrice);

                logger.LogInformation("Sell order parameters calculated!");

                await Step6CreateSellOrderAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step5CalcSellOrderParamsAsync)}, there is no active order on Stage {Stage.BuyOrderFilled}.");
            }
        }

        public async Task Step6CreateSellOrderAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var estimatedFeesPercent = appConfig.EstimatedFeesPercent;

            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset); 

            if (activeTrading != null)
            {
                if (!(new Stage[] { Stage.RollbackCancelOcoOrderCancelled, Stage.ParametersCalculated }).Any(s => s == activeTrading.Stage))
                {
                    throw new InvalidOperationException($"{nameof(Step6CreateSellOrderAsync)}: Invalid stage ({activeTrading.Stage})");
                }

                logger.LogInformation("Creating sell order!");
                await dbAdapter.UpdateSellOrderCreatingStageAsync(activeTrading.Id);

                decimal sellPrice = activeTrading.SellPrice.Value;

                if (activeTrading.IsRollback)
                {
                    sellPrice = PlusPercentage(activeTrading.BuyPrice.Value, estimatedFeesPercent);
                }
                logger.LogInformation($"Trading #{{TradingId}}. Creating sell order!");

                await BinanceSignatureOrTimestampErrorRetrierHelper(async () =>
                    await binanceApiAdapter.CreateSellOrderAsync(activeTrading.Id, holdAsset, tradeAsset, activeTrading.TradeAssetQty.Value, sellPrice, activeTrading.SellStopLimitPrice.Value, activeTrading.IsRollback));

                await Step7UpdateSellOrderCreatedStageAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step6CreateSellOrderAsync)}, there is no active order on Stage {Stage.ParametersCalculated}.");
            }
        }

        public async Task Step7UpdateSellOrderCreatedStageAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.CreatingSellOrder);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateSellOrderCreatedStageAsync(activeTrading.Id);

                logger.LogInformation($"Trading #{{TradingId}}. Sell order created!");

                await Step8WatchSellOrderAndPriceAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step7UpdateSellOrderCreatedStageAsync)}, there is no active order on Stage {Stage.CreatingSellOrder}.");
            }
        }

        public async Task Step8WatchSellOrderAndPriceAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            decimal stopThresholdIncrementPercentage = (appConfig.TargetProfitPerTradePercent - appConfig.EstimatedFeesPercent) * 0.75m;

            if(stopThresholdIncrementPercentage <= 0m)
            {
                throw new InvalidOperationException($"{nameof(stopThresholdIncrementPercentage)} <= 0");
            }

            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.SellOrderCreated);

            if (activeTrading != null)
            {
                bool firstOrderLoop = true;

                string ocoStatus;
                bool isOcoOrderActive = false;
                decimal currentPrice = decimal.MaxValue;

                bool shouldRollBack(Trading t, decimal price) => !t.IsRollback && price <= t.RollbackPrice;
                Task delayReadOrderTask = Task.CompletedTask;
                
                do
                {
                    if (!firstOrderLoop)
                    {
                        // watch price

                        currentPrice = 0m;
                        int i = 0;
                        Task delayReadPriceTask = Task.CompletedTask;
                        
                        while (i < WatchPriceMaxIterations && currentPrice < PlusPercentage(activeTrading.SellPrice ?? 0m, appConfig.TargetProfitPerTradePercent))
                        {
                            await delayReadPriceTask;
                            delayReadPriceTask = DelayAsync(WatchPriceInterval);
                            currentPrice = await RetriveCurrentPriceThenUpdateMinOrMaxPriceIfReachedAsync(activeTrading);

                            logger.LogInformation($"Trading #{{TradingId}}. Price read {{PriceRead}} ({nameof(Step8WatchSellOrderAndPriceAsync)})", activeTrading.Id, currentPrice);

                            if (shouldRollBack(activeTrading, currentPrice))
                            {
                                logger.LogInformation("Trading #{TradingId}. Rollback condition reached at current price {CurrentPrice}", activeTrading.Id, currentPrice);
                                break;
                            }

                            i++;
                        }
                    }
                    
                    // watch order
                    ocoStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, activeTrading.IsRollback);
                    await dbAdapter.UpdateSellOrderReadTime(activeTrading.Id);

                    isOcoOrderActive = ocoStatus != "ALL_DONE";

                    if (isOcoOrderActive)
                    {
                        logger.LogInformation("Trading #{TradingId}. Oco order still alive!", activeTrading.Id);

                        if (shouldRollBack(activeTrading, currentPrice))
                        {
                            logger.LogInformation("Trading #{TradingId}. Rollingback!", activeTrading.Id);

                            await Step9BeginRollbackAsync();

                            return;
                        }

                        await delayReadOrderTask;
                        delayReadOrderTask = DelayAsync(WatchOrderInterval);
                    }

                    firstOrderLoop = false;

                } while (isOcoOrderActive);

                var ocoStopLimitOrderKind = activeTrading.IsRollback ? OrderKind.SellOcoStopLimitRollbackOrder :  OrderKind.SellOcoStopLimitOrder;
                var ocoStopLimitOrder = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, ocoStopLimitOrderKind);

                var ocoLimitOrderKind = activeTrading.IsRollback ? OrderKind.SellOcoLimitRollbackOrder : OrderKind.SellOcoLimitOrder;
                var ocoLimitOrder = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, ocoLimitOrderKind);

                Order executedOrder;

                try
                {
                    executedOrder = (new Order[] { ocoStopLimitOrder, ocoLimitOrder }).Single(o => o.Status == "FILLED");
                }
                catch(Exception e)
                {
                    throw new InvalidOperationException(
                        $"Could not get a single FILLED order from {nameof(ocoStopLimitOrder)}/{nameof(ocoLimitOrder)};{Environment.NewLine}" +
                        $"{nameof(ocoStopLimitOrder)}: {JsonConvert.SerializeObject(ocoStopLimitOrder)};{Environment.NewLine}" +
                        $"{nameof(ocoLimitOrder)}: {JsonConvert.SerializeObject(ocoLimitOrder)};{Environment.NewLine}", 
                        e);
                }

                await dbAdapter.UpdateSellOrderFilledStageAsync(executedOrder);

                logger.LogInformation("Trading #{TradingId}. OCO completed!", activeTrading.Id);

                if (!activeTrading.IsRollback)
                {
                    logger.LogInformation("Trading #{TradingId}. Increasing StopThreshold by {StopThresholdIncrementPercentage}!", activeTrading.Id, stopThresholdIncrementPercentage);

                    await dbAdapter.IncreaseStopThresholdAsync(holdAsset, stopThresholdIncrementPercentage);
                }
                else
                {
                    logger.LogInformation("Trading #{TradingId}. Skiping StopThreshold increase due rolledback :/", activeTrading.Id);
                }

                _ = KeepWatchingPriceAsync(activeTrading);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step8WatchSellOrderAndPriceAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }
        }

        public async Task Step9BeginRollbackAsync()
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;

            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.SellOrderCreated);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateRollbackStageCancellingOcoOrderAsync(activeTrading.Id);
                
                logger.LogInformation($"Trading #{{TradingId}}. Cancelling oco order!");

                await BinanceSignatureOrTimestampErrorRetrierHelper(async () =>
                    await binanceApiAdapter.CancelOcoOrderAsync(activeTrading.Id, holdAsset, tradeAsset));

                await Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step9BeginRollbackAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }
        }

        public async Task Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync()
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.RollbackCancellingOcoOrder);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateRollbackStageCancelOcoOrderExecutedAsync(activeTrading.Id);

                logger.LogInformation($"Trading #{{TradingId}}. Cancel command executed!");

                await Step11RollbackCheckOcoCancelAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync)}, there is no active order on Stage {Stage.RollbackCancellingOcoOrder}.");
            }
        }

        public async Task Step11RollbackCheckOcoCancelAsync()
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTrading(holdAsset, Stage.RollbackCancelOcoOrderExecuted);

            if (activeTrading != null)
            {
                string ocoStatus;

                Task delayTask = Task.CompletedTask;

                do
                {
                    await delayTask;
                    ocoStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, isRollbackOrder: false);
                    delayTask = DelayAsync(2000);
                } while (ocoStatus != "ALL_DONE");

                await dbAdapter.UpdateRollbackStageOcoOrderCancelledAsync(activeTrading.Id);
                logger.LogInformation($"Trading #{{TradingId}}. Oco order cancelled!");

                await Step6CreateSellOrderAsync();
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step11RollbackCheckOcoCancelAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }

        }

        private async Task KeepWatchingPriceAsync(Trading activeTrading)
        {
            Task delayTask = Task.CompletedTask;

            while (!await dbAdapter.AnyActiveTradeAsync(activeTrading.HoldAsset)) 
            {
                await delayTask;
                delayTask = DelayAsync(WatchPriceInterval);
                var currentPrice = await RetriveCurrentPriceThenUpdateMinOrMaxPriceIfReachedAsync(activeTrading);

                logger.LogInformation($"Trading #{{TradingId}}. Price read {{PriceRead}} ({nameof(KeepWatchingPriceAsync)})", activeTrading.Id, currentPrice);
            }

            logger.LogInformation($"Trading #{{TradingId}}. Stopping watching price due other order activated.", activeTrading.Id);
        }

        private async Task<decimal> RetriveCurrentPriceThenUpdateMinOrMaxPriceIfReachedAsync(Trading activeTrading)
        {
            decimal currentPrice = await binanceApiAdapter.GetCurrentPrice(activeTrading.HoldAsset, activeTrading.TradeAsset);

            if (currentPrice > (activeTrading.MaxPriceRead ?? 0m))
            {
                await dbAdapter.UpdateMaxPriceAsync(activeTrading.Id, currentPrice);
                activeTrading.MaxPriceRead = currentPrice;

                logger.LogInformation("Trading #{TradingId}. Max price read {MaxPriceRead}", activeTrading.Id, currentPrice);
            }

            if (currentPrice < (activeTrading.MinPriceRead ?? decimal.MaxValue))
            {
                await dbAdapter.UpdateMinPriceAsync(activeTrading.Id, currentPrice);
                activeTrading.MinPriceRead = currentPrice;

                logger.LogInformation("Trading #{TradingId}. Min price read {MinPriceRead}", activeTrading.Id, currentPrice);
            }

            return currentPrice;
        }
    }
}
