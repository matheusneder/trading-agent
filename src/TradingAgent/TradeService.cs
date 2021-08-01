using Microsoft.Extensions.Logging;
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
        private const decimal BinanceMinNotional = 10m;
        private const decimal MinNotionalPercentageIncrement = 20m;

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
            var holdAssetToTradePercent = appConfig.HoldAssetToTradePercent;

            var processId = Guid.NewGuid().ToString();
            
            if (await dbAdapter.AnyActiveTradeAsync(holdAsset))
            {
                logger.LogInformation("Seems there is an incomplete trade running. Skiping for now ...");
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

                    decimal buyOrderQuoteQty = MinusPercentage(holdAssetBalance * (holdAssetToTradePercent / 100), 0.5m);
                    var minNotional = PlusPercentage(BinanceMinNotional, MinNotionalPercentageIncrement);

                    if (buyOrderQuoteQty < minNotional)
                    {
                        logger.LogWarning($"{nameof(buyOrderQuoteQty)} < {nameof(BinanceMinNotional)}. Skipping trade.");
                    }
                    else
                    {
                        if (await dbAdapter.IsTradeMinimumAmountModeActiveAsync(holdAsset))
                        {
                            buyOrderQuoteQty = minNotional;

                            logger.LogWarning("TradeMinimumAmountMode is Active, going to buy just near the minimum amount allowed.");
                        }

                        logger.LogInformation("BuyOrderQuoteQty: {BuyOrderQuoteQty}", buyOrderQuoteQty);

                        tradingId = await dbAdapter.InsertNewOperationAsync(holdAsset, tradeAsset, buyOrderQuoteQty, processId);

                        await Step2CreateBuyOrderAsync(processId);
                    }
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
            var upgradePricePercent = appConfig.UpgradePriceTriggerPercent;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.BuyOrderFilled, processId);


            if (activeTrading != null)
            {
                decimal buyPrice = activeTrading.BuyPrice ?? throw new InvalidOperationException($"{nameof(Trading.BuyPrice)} is null");
                decimal sellPrice = PlusPercentage(buyPrice, targetProfitPerTradePercent);
                decimal sellStopLimitPrice = MinusPercentage(buyPrice, stopLossPercent);
                decimal rollbackPrice = MinusPercentage(buyPrice, rollbackPricePercent);
                decimal upgradePrice = PlusPercentage(buyPrice, upgradePricePercent);

                await dbAdapter.UpdateSellOrderParametersCalculatedStageAsync(activeTrading.Id, sellPrice, sellStopLimitPrice, 
                    rollbackPrice: rollbackPrice, 
                    upgradePrice: upgradePrice, 
                    processId: processId);

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
            //var estimatedFeesPercent = appConfig.EstimatedFeesPercent;
            //var targetProfitPerTradePercent = appConfig.TargetProfitPerTradePercent;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, processId: processId); 

            if (activeTrading != null)
            {
                if (!(new Stage[] { Stage.RollbackOrUpgradeCancelOcoOrderCancelled, Stage.ParametersCalculated }).Any(s => s == activeTrading.Stage))
                {
                    throw new InvalidOperationException($"{nameof(Step6CreateSellOrderAsync)}: Invalid stage ({activeTrading.Stage})");
                }

                logger.LogInformation("Creating sell order!");
                
                var sellOrderBinanceIdSuffix = Guid.NewGuid().ToString().Replace("-", string.Empty).Substring(0, 20);

                await dbAdapter.UpdateSellOrderCreatingStageAsync(activeTrading.Id, sellOrderBinanceIdSuffix, processId);

                //decimal sellPrice = activeTrading.SellPrice.Value;

                //if (activeTrading.IsRollback)
                //{
                //    sellPrice = MinusPercentage(sellPrice, targetProfitPerTradePercent - estimatedFeesPercent);
                //}

                logger.LogInformation($"Trading #{{TradingId}}. Creating sell order!", activeTrading.Id);

                await BinanceSignatureOrTimestampErrorRetrierHelperAsync(async () =>
                    await binanceApiAdapter.CreateSellOrderAsync(activeTrading.Id, holdAsset, tradeAsset, activeTrading.TradeAssetQty.Value, activeTrading.SellPrice.Value, activeTrading.SellStopLimitPrice.Value, sellOrderBinanceIdSuffix));

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
                bool shouldUpgrade(Trading t, decimal price) => !t.IsRollback && price >= t.UpgradePrice && /* TODO: fix this codesmell */ price != decimal.MaxValue;

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
                            
                            if(shouldUpgrade(activeTrading, currentPrice))
                            {
                                logger.LogInformation("Trading #{TradingId}. Upgrade condition reached at current price {CurrentPrice}", activeTrading.Id, currentPrice);
                                break;
                            }

                            i++;
                        }
                    }
                    
                    // watch order
                    ocoStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, activeTrading.SellOrderBinanceIdSuffix);
                    await dbAdapter.UpdateSellOrderReadTimeAsync(activeTrading.Id, processId);

                    if(ocoStatus == null)
                    {
                        logger.LogDebug("Trading #{TradingId}. Read NULL Oco order (not found). Assume it is being created (I hope) ... Will fail if read NULL more than 2 times.", activeTrading.Id);

                        if (readConsecutiveStatusNullCount > 2)
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
                        logger.LogInformation("Trading #{TradingId}. Oco order alive!", activeTrading.Id);

                        if (shouldRollback(activeTrading, currentPrice))
                        {
                            logger.LogInformation("Trading #{TradingId}. Rollingback!", activeTrading.Id);

                            await Step9BeginRollbackOrUpgradeAsync(true, processId);

                            return;
                        }

                        if (shouldUpgrade(activeTrading, currentPrice))
                        {
                            logger.LogInformation("Trading #{TradingId}. Upgrading!", activeTrading.Id);

                            await Step9BeginRollbackOrUpgradeAsync(false, processId);

                            return;
                        }

                        await delayReadOrderTask;
                        delayReadOrderTask = DelayAsync(WatchOrderInterval);
                    }

                    firstOrderLoop = false;

                } while (isOcoOrderActive);

                var ocoStopLimitOrderKind = activeTrading.IsRollback ? OrderKind.SellOcoStopLimitRollbackOrder :  OrderKind.SellOcoStopLimitOrder;
                var ocoStopLimitOrder = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, ocoStopLimitOrderKind, sellOrderBinanceIdSuffix: activeTrading.SellOrderBinanceIdSuffix);

                var ocoLimitOrderKind = activeTrading.IsRollback ? OrderKind.SellOcoLimitRollbackOrder : OrderKind.SellOcoLimitOrder;
                var ocoLimitOrder = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, ocoLimitOrderKind, sellOrderBinanceIdSuffix: activeTrading.SellOrderBinanceIdSuffix);

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

                await UpdateStopLossControldAsync(activeTrading.Id);

                _ = KeepWatchingPriceAsync(activeTrading, processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step8WatchSellOrderAndPriceAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }
        }

        private async Task UpdateStopLossControldAsync(int trandingId)
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
                
                await dbAdapter.SetTradeMinimumAmountModeActiveAsync(completedTrading.HoldAsset, true);

                logger.LogWarning("MinimumAmountMode activeted... Next trades will spent just near the minimum amount allowed.");
            }
        }

        public async Task Step9BeginRollbackOrUpgradeAsync(bool isRollback /* assume is upgrade if false*/, string processId)
        {
            var holdAsset = appConfig.HoldAsset;
            var tradeAsset = appConfig.TradeAsset;
            var targetProfitPerTradePercent = appConfig.TargetProfitPerTradePercent;
            var estimatedFeesPercent = appConfig.EstimatedFeesPercent;
            var upgradePriceIncrementPercent = appConfig.UpgradePriceIncrementPercent;
            var upgradePriceTriggerPercent = appConfig.UpgradePriceTriggerPercent;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.SellOrderCreated);

            if (activeTrading != null)
            {
                if (isRollback) 
                {
                    decimal newSellPrice = MinusPercentage(activeTrading.SellPrice.Value, targetProfitPerTradePercent - estimatedFeesPercent);

                    await dbAdapter.UpdateRollbackStageCancellingOcoOrderAsync(activeTrading.Id, newSellPrice, processId);
                }
                else
                {
                    // upgrade
                    decimal buyPrice = activeTrading.BuyPrice.Value;
                    //decimal oldSellPrice = activeTrading.SellPrice.Value;
                    decimal newSellPrice;// = buyPrice * 2m;
                    //decimal incrementAmount = newSellPrice - oldSellPrice;
                    decimal newRollbackPrice = activeTrading.RollbackPrice.Value;

                    decimal newUpgradePrice;// = activeTrading.UpgradePrice.Value + incrementAmount;

                    decimal newSellStopLimitPrice;
                    //decimal oldSellStopLimitPrice = activeTrading.SellStopLimitPrice.Value;

                    if(activeTrading.UpgradeCount == 0)
                    {
                        newSellPrice = buyPrice * 2m;
                        newSellStopLimitPrice = PlusPercentage(buyPrice, targetProfitPerTradePercent - 0.2m);
                        newUpgradePrice = PlusPercentage(buyPrice, targetProfitPerTradePercent + upgradePriceTriggerPercent); // 0.85 + 0.72 (do buy price)
                    }
                    else if(activeTrading.UpgradeCount == 1)
                    {
                        newSellPrice = buyPrice * 2m;
                        newSellStopLimitPrice = PlusPercentage(buyPrice, targetProfitPerTradePercent);
                        newUpgradePrice = PlusPercentage(newSellStopLimitPrice, appConfig.RollbackPricePercent); // 1.25
                    }
                    else
                    {
                        newSellPrice = buyPrice * 3m;
                        newSellStopLimitPrice = PlusPercentage(activeTrading.SellStopLimitPrice.Value,
                                MinusPercentage(upgradePriceIncrementPercent, Math.Min(activeTrading.UpgradeCount, 25)));
                        newUpgradePrice = PlusPercentage(activeTrading.UpgradePrice.Value, upgradePriceIncrementPercent); // 0.5
                    }

                        await dbAdapter
                        .UpdateUpgradeStageCacellingOcoOrderAsync(activeTrading.Id, newSellPrice, newRollbackPrice, newSellStopLimitPrice, newUpgradePrice, processId);
                }
                
                logger.LogInformation($"Trading #{{TradingId}}. Cancelling oco order!");

                await BinanceSignatureOrTimestampErrorRetrierHelperAsync(async () =>
                    await binanceApiAdapter.CancelOcoOrderAsync(activeTrading.Id, holdAsset, tradeAsset, activeTrading.SellOrderBinanceIdSuffix));

                await Setp10UpdateRollbackOrUpgradeStageCancelOcoOrderExecutedAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step9BeginRollbackOrUpgradeAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
            }
        }

        public async Task Setp10UpdateRollbackOrUpgradeStageCancelOcoOrderExecutedAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.RollbackOrUpgradeCancellingOcoOrder);

            if (activeTrading != null)
            {
                await dbAdapter.UpdateRollbackOrUpgradeStageCancelOcoOrderExecutedAsync(activeTrading.Id, processId);

                logger.LogInformation($"Trading #{{TradingId}}. Cancel command executed!");

                await Step11RollbackOrUpgradeCheckOcoCancelAsync(processId);
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Setp10UpdateRollbackOrUpgradeStageCancelOcoOrderExecutedAsync)}, there is no active order on Stage {Stage.RollbackOrUpgradeCancellingOcoOrder}.");
            }
        }

        public async Task Step11RollbackOrUpgradeCheckOcoCancelAsync(string processId)
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading = await dbAdapter.GetActiveTradingAsync(holdAsset, Stage.RollbackOrUpgradeCancelOcoOrderExecuted);

            if (activeTrading != null)
            {
                string ocoStatus;

                Task delayTask = Task.CompletedTask;
                int readConsecutiveStatusNullCount = 0;

                do
                {
                    await delayTask;
                    ocoStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, activeTrading.SellOrderBinanceIdSuffix);
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

                var ocoLimitOrder = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, OrderKind.SellOcoLimitOrder, activeTrading.SellOrderBinanceIdSuffix);
                var ocoStoptOrder = await binanceApiAdapter.GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, OrderKind.SellOcoStopLimitOrder, activeTrading.SellOrderBinanceIdSuffix);

                // Ensure order cancelled
                if (ocoLimitOrder.Status == "CANCELED" && ocoStoptOrder.Status == "CANCELED")
                {
                    await dbAdapter.UpdateRollbackOrUpgradeStageOcoOrderCancelledAsync(activeTrading.Id, processId);
                    logger.LogInformation($"Trading #{{TradingId}}. Oco order cancelled!");

                    await Step6CreateSellOrderAsync(processId);
                }
                else
                {
                    logger.LogWarning($"Trading #{activeTrading.Id}. Order cancellation failed: Sounds it has been executed before cancel.", activeTrading.Id);
                    
                    await dbAdapter.UpdateSellOrderCreatedStageAsync(activeTrading.Id, processId);
                    
                    await Step8WatchSellOrderAndPriceAsync(processId);
                }
            }
            else
            {
                logger.LogInformation($"Skiping {nameof(Step11RollbackOrUpgradeCheckOcoCancelAsync)}, there is no active order on Stage {Stage.SellOrderCreated}.");
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
