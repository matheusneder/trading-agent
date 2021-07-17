﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
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
        // TODO: Review
        internal static bool EnjBusdPrepared = false;

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

            var processId = Guid.NewGuid().ToString();

            if (!EnjBusdPrepared)
            {
                logger.LogInformation("Trade not prepared! Skipping....");
            }
            else if (await dbAdapter.AnyActiveTradeAsync(holdAsset))
            {
                logger.LogInformation("Seems there is an incomplete trade running. Skiping for now...");
            }
            else
            {
                EnjBusdPrepared = false;
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

                    tradingId = await dbAdapter.InsertNewOperationAsync(holdAsset, tradeAsset, buyOrderQuoteQty, processId);

                    await Step2CreateBuyOrderAsync(processId);
                }
                else
                {
                    logger.LogWarning(
                        $"sellStopQuoteQty < stopTradingThreshold ({sellStopQuoteQtyOverTotalBalance} < {stopTradingThreshold}); PERMANENTLY SKIPING trading ...");
                }
            }

            return tradingId;
        }

        public async Task Step2CreateBuyOrderAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.JustRegistered, processId);

            if (activeTrading != null)
            {
                if (activeTrading.CreatedAt.AddSeconds(20) < DateTimeOffset.Now)
                {
                    logger.LogWarning("Skiping create buy order due old order register (CreateAt). Updating it as completed.", activeTrading.CreatedAt);

                    await dbAdapter.UpdateTradeCompletedAndNotInitializedStageAsync(activeTrading.Id, "Skiping create buy order due old order register", processId);
                }
                else
                {
                    await dbAdapter.UpdateTradingCreatingBuyOrderStageAsync(activeTrading.Id, processId);
                    
                    logger.LogInformation($"Trading #{{TradingId}}. Creating buy order!");

                    await BinanceSignatureOrTimestampErrorRetrierHelperAsync(async () => 
                        await binanceApiAdapter.CreateBuyOrderAsync(activeTrading.Id, holdAsset, tradeAsset, activeTrading.BuyOrderQuoteQty));

                    await Step3UpdateOrderCreatedStageAsync(processId);
                }
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step2CreateBuyOrderAsync)}, there is no active order on Stage {Stage.JustRegistered}.");
            }
        }

        private async Task BinanceSignatureOrTimestampErrorRetrierHelperAsync(Func<Task> action)
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
   

        public async Task Step3UpdateOrderCreatedStageAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.CreatingBuyOrder, processId);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateOrderCreatedStageAsync(activeTrading.Id, processId);
                logger.LogInformation($"Trading #{{TradingId}}. Buy order created!");

                await Step4RetriveBuyOrderDataAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step3UpdateOrderCreatedStageAsync)}, there is no active order on Stage {Stage.CreatingBuyOrder}.");
            }
        }

        public async Task Step4RetriveBuyOrderDataAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.BuyOrderCreated, processId);

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

                await dbAdapter.UpdateBuyOrderFilledStageAsync(order, processId);

                logger.LogInformation("Buy order executed!");

                await Step5CalcSellOrderParamsAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step4RetriveBuyOrderDataAsync)}, there is no active order on Stage {Stage.BuyOrderCreated}.");
            }
        }

        private decimal PlusPercentage(decimal value, decimal percentage) => value * (1 + percentage / 100);
        private decimal MinusPercentage(decimal value, decimal percentage) => value - (value * percentage / 100);

        public async Task Step5CalcSellOrderParamsAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var targetProfitPerTradePercent = appConfig.TargetProfitPerTradePercent;
            var stopLossPercent = appConfig.StopLossPercent;
            var rollbackPricePercent = appConfig.RollbackPricePercent;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.BuyOrderFilled, processId);


            if (activeTrading != null)
            {
                decimal buyPrice = activeTrading.BuyPrice ?? throw new InvalidOperationException($"{nameof(Trading.BuyPrice)} is null");
                decimal sellPrice = PlusPercentage(buyPrice, targetProfitPerTradePercent);
                decimal sellStopLimitPrice = MinusPercentage(buyPrice, stopLossPercent);
                decimal rollbackPrice = MinusPercentage(buyPrice, rollbackPricePercent);

                await dbAdapter.UpdateSellOrderParametersCalculatedStageAsync(activeTrading.Id, sellPrice, sellStopLimitPrice, rollbackPrice, processId);

                logger.LogInformation("Sell order parameters calculated!");

                await Step6CreateSellOrderAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step5CalcSellOrderParamsAsync)}, there is no active order on Stage {Stage.BuyOrderFilled}.");
            }
        }

        public async Task Step6CreateSellOrderAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var estimatedFeesPercent = appConfig.EstimatedFeesPercent;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, processId: processId); 

            if (activeTrading != null)
            {
                if (!(new Stage[] { Stage.RollbackCancelOcoOrderCancelled, Stage.ParametersCalculated }).Any(s => s == activeTrading.Stage))
                {
                    throw new InvalidOperationException($"{nameof(Step6CreateSellOrderAsync)}: Invalid stage ({activeTrading.Stage})");
                }

                logger.LogInformation("Creating sell order!");
                await dbAdapter.UpdateSellOrderCreatingStageAsync(activeTrading.Id, processId);

                decimal sellPrice = activeTrading.SellPrice.Value;

                if (activeTrading.IsRollback)
                {
                    sellPrice = PlusPercentage(activeTrading.BuyPrice.Value, estimatedFeesPercent);
                }
                logger.LogInformation($"Trading #{{TradingId}}. Creating sell order!", activeTrading.Id);

                await BinanceSignatureOrTimestampErrorRetrierHelperAsync(async () =>
                    await binanceApiAdapter.CreateSellOrderAsync(activeTrading.Id, holdAsset, tradeAsset, activeTrading.TradeAssetQty.Value, sellPrice, activeTrading.SellStopLimitPrice.Value, activeTrading.IsRollback));

                await Step7UpdateSellOrderCreatedStageAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step6CreateSellOrderAsync)}, there is no active order on Stage {Stage.ParametersCalculated}.");
            }
        }

        public async Task Step7UpdateSellOrderCreatedStageAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.CreatingSellOrder, processId);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateSellOrderCreatedStageAsync(activeTrading.Id, processId);

                logger.LogInformation($"Trading #{{TradingId}}. Sell order created!", activeTrading.Id);

                await Step8WatchSellOrderAndPriceAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step7UpdateSellOrderCreatedStageAsync)}, there is no active order on Stage {Stage.CreatingSellOrder}.");
            }
        }

        public async Task Step8WatchSellOrderAndPriceAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.SellOrderCreated, processId);

            if (activeTrading != null)
            {
                bool firstOrderLoop = true;

                string ocoStatus;
                bool isOcoOrderActive = false;
                decimal currentPrice = decimal.MaxValue;

                bool shouldRollback(Trading t, decimal price) => !t.IsRollback && price <= t.RollbackPrice;
                Task delayReadOrderTask = Task.CompletedTask;
                int readConsecutiveStatusNullCount = 0;

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
                            currentPrice = await RetriveCurrentPriceThenUpdateMinOrMaxPriceIfReachedAsync(activeTrading, processId);

                            logger.LogDebug($"Trading #{{TradingId}}. Price read {{PriceRead}} ({nameof(Step8WatchSellOrderAndPriceAsync)})", activeTrading.Id, currentPrice);

                            if (shouldRollback(activeTrading, currentPrice))
                            {
                                logger.LogInformation("Trading #{TradingId}. Rollback condition reached at current price {CurrentPrice}", activeTrading.Id, currentPrice);
                                break;
                            }

                            i++;
                        }
                    }
                    
                    // watch order
                    ocoStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, activeTrading.IsRollback);
                    await dbAdapter.UpdateSellOrderReadTimeAsync(activeTrading.Id, processId);

                    if(ocoStatus == null)
                    {
                        if(readConsecutiveStatusNullCount > 2)
                        {
                            throw new InvalidOperationException($"Trading #{activeTrading.Id}. Oco status null!");
                        }

                        readConsecutiveStatusNullCount++;
                    }
                    else
                    {
                        readConsecutiveStatusNullCount = 0;
                    }

                    if(ocoStatus == "REJECTED")
                    {
                        throw new InvalidOperationException($"Trading #{activeTrading.Id}. Unexpected oco order status: {ocoStatus}");
                    }

                    isOcoOrderActive = ocoStatus != "ALL_DONE";

                    if (isOcoOrderActive)
                    {
                        logger.LogInformation("Trading #{TradingId}. Oco order still alive!", activeTrading.Id);

                        if (shouldRollback(activeTrading, currentPrice))
                        {
                            logger.LogInformation("Trading #{TradingId}. Rollingback!", activeTrading.Id);

                            await Step9BeginRollbackAsync(processId);

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

                await dbAdapter.UpdateSellOrderFilledStageAsync(executedOrder, processId);

                logger.LogInformation("Trading #{TradingId}. OCO completed!", activeTrading.Id);

                await UpdateStopThresholdAsync(activeTrading.Id);

                _ = KeepWatchingPriceAsync(activeTrading, processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step8WatchSellOrderAndPriceAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }
        }

        private async Task UpdateStopThresholdAsync(int trandingId)
        {
            decimal estimatedFeesPercent = appConfig.EstimatedFeesPercent;
            Trading completedTrading = await dbAdapter.GetTradingAsync(trandingId);
            var sellOrderExecutedPrice = completedTrading.SellOrderExecutedPrice ?? throw new InvalidOperationException($"Expected {nameof(Trading.SellOrderExecutedPrice)} not null");
            var tradeAssetQty = completedTrading.TradeAssetQty ?? throw new InvalidOperationException($"Expected {nameof(Trading.TradeAssetQty)} not null");
            decimal tradeEarns = sellOrderExecutedPrice * tradeAssetQty - completedTrading.BuyOrderQuoteQty;

            if(tradeEarns > 0m)
            {
                logger.LogInformation("Trading #{TradingId}. Completed trade had profits, earnin: {TradeEarns} {HoldAsset}", 
                    completedTrading.Id, 
                    tradeEarns, 
                    completedTrading.HoldAsset);

                var estimatedTradeFees = completedTrading.BuyOrderQuoteQty * (estimatedFeesPercent / 100);
                var stopThreasholdIncrement = (tradeEarns - estimatedTradeFees) * 0.8m;

                if(stopThreasholdIncrement > 0m)
                {
                    logger.LogInformation("Trading #{TradingId}. Incrementing stopThreashold by {StopThreasholdIncrement}", completedTrading.Id, stopThreasholdIncrement);
                    
                    await dbAdapter.IncreamentStopThresholdAsync(completedTrading.HoldAsset, stopThreasholdIncrement);
                }
                else
                {
                    logger.LogWarning("Trading #{TradingId}. " +
                        "Skipping stopThreasholdIncrement due trade earns {TradeEarns} < estimated trade fees {EstimatedTradeFees}.",
                            completedTrading.Id, tradeEarns, estimatedTradeFees);
                }
            }
            else
            {
                logger.LogWarning("Trading #{TradingId}. Completed trade had loss: {TradeEarns} {HoldAsset}", completedTrading.Id, tradeEarns * -1, completedTrading.HoldAsset);
            }
        }

        public async Task Step9BeginRollbackAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.SellOrderCreated);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateRollbackStageCancellingOcoOrderAsync(activeTrading.Id, processId);
                
                logger.LogInformation($"Trading #{{TradingId}}. Cancelling oco order!");

                await BinanceSignatureOrTimestampErrorRetrierHelperAsync(async () =>
                    await binanceApiAdapter.CancelOcoOrderAsync(activeTrading.Id, holdAsset, tradeAsset));

                await Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step9BeginRollbackAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }
        }

        public async Task Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.RollbackCancellingOcoOrder);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateRollbackStageCancelOcoOrderExecutedAsync(activeTrading.Id, processId);

                logger.LogInformation($"Trading #{{TradingId}}. Cancel command executed!");

                await Step11RollbackCheckOcoCancelAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync)}, there is no active order on Stage {Stage.RollbackCancellingOcoOrder}.");
            }
        }

        public async Task Step11RollbackCheckOcoCancelAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.RollbackCancelOcoOrderExecuted);

            if (activeTrading != null)
            {
                string ocoStatus;

                Task delayTask = Task.CompletedTask;
                int readConsecutiveStatusNullCount = 0;

                do
                {
                    await delayTask;
                    ocoStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, isRollbackOrder: false);
                    delayTask = DelayAsync(2000);

                    if (ocoStatus == null)
                    {
                        if (readConsecutiveStatusNullCount > 2)
                        {
                            throw new InvalidOperationException($"Trading #{activeTrading.Id}. Oco status null while cancelling order!");
                        }

                        readConsecutiveStatusNullCount++;
                    }
                    else
                    {
                        readConsecutiveStatusNullCount = 0;
                    }

                    if (ocoStatus == "REJECTED")
                    {
                        throw new InvalidOperationException($"Trading #{activeTrading.Id}. Unexpected oco order status while cacelling order: {ocoStatus}");
                    }
                } while (ocoStatus != "ALL_DONE");

                await dbAdapter.UpdateRollbackStageOcoOrderCancelledAsync(activeTrading.Id, processId);
                logger.LogInformation($"Trading #{{TradingId}}. Oco order cancelled!");

                await Step6CreateSellOrderAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step11RollbackCheckOcoCancelAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }

        }

        private async Task KeepWatchingPriceAsync(Trading activeTrading, string processId)
        {
            Task delayTask = Task.CompletedTask;

            try
            {
                while (!await dbAdapter.AnyActiveTradeAsync(activeTrading.HoldAsset))
                {
                    await delayTask;
                    delayTask = DelayAsync(WatchPriceInterval);
                    var currentPrice = await RetriveCurrentPriceThenUpdateMinOrMaxPriceIfReachedAsync(activeTrading, processId);

                    logger.LogDebug($"Trading #{{TradingId}}. Price read {{PriceRead}} ({nameof(KeepWatchingPriceAsync)})", activeTrading.Id, currentPrice);
                }

                logger.LogInformation($"Trading #{{TradingId}}. Stopping watching price due other order activated.", activeTrading.Id);
            }
            catch(Exception e)
            {
                logger.LogError(e, $"Error on {nameof(KeepWatchingPriceAsync)}. Stopping watching!");
            }
        }

        private async Task<decimal> RetriveCurrentPriceThenUpdateMinOrMaxPriceIfReachedAsync(Trading activeTrading, string processId)
        {
            decimal currentPrice = await binanceApiAdapter.GetCurrentPrice(activeTrading.HoldAsset, activeTrading.TradeAsset);

            if (currentPrice > (activeTrading.MaxPriceRead ?? 0m))
            {
                await dbAdapter.UpdateMaxPriceAsync(activeTrading.Id, currentPrice, processId);
                activeTrading.MaxPriceRead = currentPrice;

                logger.LogInformation("Trading #{TradingId}. Max price read {MaxPriceRead}", activeTrading.Id, currentPrice);
            }

            if (currentPrice < (activeTrading.MinPriceRead ?? decimal.MaxValue))
            {
                await dbAdapter.UpdateMinPriceAsync(activeTrading.Id, currentPrice, processId);
                activeTrading.MinPriceRead = currentPrice;

                logger.LogInformation("Trading #{TradingId}. Min price read {MinPriceRead}", activeTrading.Id, currentPrice);
            }

            return currentPrice;
        }
    }
}
