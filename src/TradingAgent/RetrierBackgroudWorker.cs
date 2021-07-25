using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public class RetrierBackgroudWorker : BackgroundService
    {
        private readonly ILogger logger;
        private readonly TradeService tradeService;
        private readonly DbAdapter dbAdapter;
        private readonly BinanceApiAdapter binanceApiAdapter;
        private readonly AppConfig appConfig;

        public RetrierBackgroudWorker(ILoggerFactory loggerFactory, TradeService tradeService, DbAdapter dbAdapter, BinanceApiAdapter binanceApiAdapter, AppConfig appConfig)
        {
            logger = loggerFactory.CreateLogger<RetrierBackgroudWorker>();
            this.tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));
            this.dbAdapter = dbAdapter ?? throw new ArgumentNullException(nameof(dbAdapter));
            this.binanceApiAdapter = binanceApiAdapter ?? throw new ArgumentNullException(nameof(binanceApiAdapter));
            this.appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckIncompleteTradeAsync();
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, $"{nameof(RetrierBackgroudWorker)}.{nameof(ExecuteAsync)} error: {e.Message}");
                }

                await Task.Delay(5000);
            }
        }

        private async Task CheckIncompleteTradeAsync()
        {
            var holdAsset = appConfig.HoldAsset;

            var activeTrading  = await dbAdapter.GetActiveTradingAsync(holdAsset);

            if (activeTrading != null)
            {
                var updatedAt = activeTrading.UpdatedAt ?? throw new InvalidOperationException("Expected UpdatedAt not null");

                if (updatedAt.AddSeconds(65) < DateTimeOffset.Now)
                {
                    await HandleIncompleteTradeAsync(activeTrading);
                }
            }
        }

        private async Task<string> SwitchProcessIdAsync(Trading activeTrading)
        {
            string newProcessId = $"{Guid.NewGuid().ToString()}-rec";
            await dbAdapter.UpdateTradingProcessIdAsync(activeTrading.Id, newProcessId);

            return newProcessId;
        }

        private async Task HandleIncompleteTradeAsync(Trading activeTrading)
        {
            logger.LogWarning("Found incomplete trading #{TradingId}, trying to resume it...", activeTrading.Id);

            switch (activeTrading.Stage)
            {
                case Stage.JustRegistered:
                    logger.LogError($"#{{TradingId}} Unexpected stage {{Stage}}", activeTrading.Id, activeTrading.Stage);
                    break;
                case Stage.CreatingBuyOrder:
                    await HandleIncompleteCreatingBuyOrderAsync(activeTrading);
                    break;
                case Stage.BuyOrderCreated:
                    logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: {nameof(TradeService.Step4RetriveBuyOrderDataAsync)}", activeTrading.Id, activeTrading.Stage);
                    await tradeService.Step4RetriveBuyOrderDataAsync(await SwitchProcessIdAsync(activeTrading));
                    break;
                case Stage.BuyOrderFilled:
                    logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: {nameof(TradeService.Step5CalcSellOrderParamsAsync)}", activeTrading.Id, activeTrading.Stage);
                    await tradeService.Step5CalcSellOrderParamsAsync(await SwitchProcessIdAsync(activeTrading));
                    break;
                case Stage.ParametersCalculated:
                case Stage.RollbackCancelOcoOrderCancelled:
                    logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: {nameof(TradeService.Step6CreateSellOrderAsync)}", activeTrading.Id, activeTrading.Stage);
                    await tradeService.Step6CreateSellOrderAsync(await SwitchProcessIdAsync(activeTrading));
                    break;
                case Stage.CreatingSellOrder:
                    await HandleIncompleteCreatingSellOrderAsync(activeTrading);
                    break;
                case Stage.SellOrderCreated:
                    logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: {nameof(TradeService.Step8WatchSellOrderAndPriceAsync)}", activeTrading.Id, activeTrading.Stage);
                    await tradeService.Step8WatchSellOrderAndPriceAsync(await SwitchProcessIdAsync(activeTrading));
                    break;
                case Stage.SellOrderFilled:
                    logger.LogError($"#{{TradingId}} Unexpected stage {{Stage}}", activeTrading.Id, activeTrading.Stage);
                    break;
                case Stage.RollbackCancellingOcoOrder:
                    await HandleIncompleteRollbackCancellingOcoOrderAsync(activeTrading);
                    break;
                case Stage.RollbackCancelOcoOrderExecuted:
                    logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: {nameof(TradeService.Step11RollbackCheckOcoCancelAsync)}", activeTrading.Id, activeTrading.Stage);
                    await tradeService.Step11RollbackCheckOcoCancelAsync(await SwitchProcessIdAsync(activeTrading));
                    break;
            }
        }

        private async Task HandleIncompleteRollbackCancellingOcoOrderAsync(Trading activeTrading)
        {
            logger.LogWarning($"Trading #{{TradingId}} :: incomplete trade on {nameof(Stage.RollbackCancellingOcoOrder)} stage has detected.");

            var sellOrderStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, activeTrading.IsRollback);

            if (sellOrderStatus != null)
            {
                switch (sellOrderStatus)
                {
                    case "REJECTED":

                        logger.LogError(
                            $"Trading #{{TradingId}}: Inconsistent status reported by binance: {{SellOrderStatus}} (cacelling the order, it must have been created before)",
                            activeTrading.Id, sellOrderStatus);
                        
                        break;
                    case "EXECUTING":
                        
                        try
                        {
                            var newProcessId = await SwitchProcessIdAsync(activeTrading);

                            logger.LogWarning($"Trading #{{TradingId}}: Oco order still executing, requesting cancellation again!", activeTrading.Id); 

                            await binanceApiAdapter.CancelOcoOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset);

                            logger.LogInformation($"Cancel request worked! Resuming #{{TradingId}}; Stage {{Stage}} :: " +
                                $"{nameof(TradeService.Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync)}", activeTrading.Id, activeTrading.Stage);

                            await tradeService.Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync(newProcessId);
                        }
                        catch(Exception e)
                        {
                            logger.LogWarning(e, $"Trading #{{TradingId}}: Exception has ocurred while trying to cacel order/resume {nameof(TradeService.Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync)}", activeTrading.Id);
                        }
                        
                        break;
                    case "ALL_DONE":

                        logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: " +
                            $"{nameof(TradeService.Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync)}", activeTrading.Id, activeTrading.Stage);

                        await tradeService.Setp10UpdateRollbackStageCancelOcoOrderExecutedAsync(await SwitchProcessIdAsync(activeTrading));

                        break;
                    default:

                        logger.LogError("Sell order (OCO) status {Status} is unmapped!", sellOrderStatus);
                        
                        break;
                }
            }
            else
            {
                logger.LogError("Trying to cancel order but binance says it not found (this is an inconsistent state!).");
            }
        }

        private async Task HandleIncompleteCreatingSellOrderAsync(Trading activeTrading)
        {
            var sellOrderCreatingTimeoutSeconds = 65;

            logger.LogWarning($"Trading #{{TradingId}} :: incomplete trade on {nameof(Stage.CreatingSellOrder)} stage has detected.", activeTrading.Id);

            var sellOrderStatus = await binanceApiAdapter.GetOcoOrderStatusAsync(activeTrading.Id, activeTrading.IsRollback);

            if(sellOrderStatus != null)
            {
                switch (sellOrderStatus)
                {
                    case "REJECTED":

                        logger.LogWarning(
                            $"Trading #{{TradingId}}: Binance says that the order was created, but status is {{SellOrderStatus}}. ", 
                            activeTrading.Id, sellOrderStatus);
                        
                        await PlaceANewSellOrderAsync(activeTrading);
                        
                        break;
                    case "EXECUTING":
                    case "ALL_DONE":
                        
                        logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: " +
                            $"{nameof(TradeService.Step7UpdateSellOrderCreatedStageAsync)}", activeTrading.Id, activeTrading.Stage);
                        
                        await tradeService.Step7UpdateSellOrderCreatedStageAsync(await SwitchProcessIdAsync(activeTrading));
                        
                        break;
                    default:
                        logger.LogError("Sell order (OCO) status {Status} is unmapped!", sellOrderStatus);
                        break;
                }
            }
            else
            {
                if (activeTrading.UpdatedAt?.AddSeconds(sellOrderCreatingTimeoutSeconds) < DateTimeOffset.Now)
                {
                    logger.LogWarning("Order CREATE has requested more than {SellOrderCreatingTimeoutSeconds} seconds ago but binance says it not found.", 
                        sellOrderCreatingTimeoutSeconds);
                    
                    await PlaceANewSellOrderAsync(activeTrading);
                }
                else
                {
                    logger.LogWarning(
                        $"Trading #{{TradingId}}: Despite binance says that the SELL order not found, will skip for now in order to verify again in few seconds " +
                        $"to ensure the order was not created.", activeTrading.Id);
                }
            }
        }

        private async Task PlaceANewSellOrderAsync(Trading activeTrading)
        {
            logger.LogWarning($"Trading #{{TradingId}}: Changing the trade stage to {nameof(Stage.ParametersCalculated)} then calling " +
                            $"{nameof(TradeService.Step6CreateSellOrderAsync)} in order to try to place a new one!", activeTrading.Id);

            var sellPrice = activeTrading.SellPrice ?? throw new InvalidOperationException($"Expected {nameof(Trading.SellPrice)} not null");
            var sellStopLimitPrice = activeTrading.SellStopLimitPrice ?? throw new InvalidOperationException($"Expected {nameof(Trading.SellStopLimitPrice)} not null");
            var upgradePrice = activeTrading.UpgradePrice ?? throw new InvalidOperationException($"Expected {nameof(Trading.UpgradePrice)} not null");
            var rollbackPrice = activeTrading.RollbackPrice ?? throw new InvalidOperationException($"Expected {nameof(Trading.RollbackPrice)} not null");
            
            var newProcessId = await SwitchProcessIdAsync(activeTrading);
            
            await dbAdapter.UpdateSellOrderParametersCalculatedStageAsync(activeTrading.Id,
                sellPrice,
                sellStopLimitPrice,
                rollbackPrice: rollbackPrice,
                upgradePrice: upgradePrice,
                processId: newProcessId);

            await tradeService.Step6CreateSellOrderAsync(newProcessId);
        }

        private async Task HandleIncompleteCreatingBuyOrderAsync(Trading activeTrading)
        {
            var buyOrderCreatingTimeoutSeconds = 180;

            logger.LogWarning($"Trading #{{TradingId}} :: incomplete trade on {nameof(Stage.CreatingBuyOrder)} stage has detected.");

            var buyOrder = await binanceApiAdapter
                .GetOrderAsync(activeTrading.Id, activeTrading.HoldAsset, activeTrading.TradeAsset, OrderKind.BuyMarketOrder);

            if(buyOrder != null)
            {
                switch (buyOrder.Status)
                {
                    case "CANCELED":
                    case "REJECTED":
                    case "EXPIRED":

                        logger.LogWarning("Trading #{TradingId} :: discarding trade due buy order status: {Status}", 
                            activeTrading.Id, buyOrder.Status);
                        
                        await dbAdapter.UpdateTradeCompletedAndNotInitializedStageAsync(activeTrading.Id, abortReason: $"Buy order {buyOrder.Status}", await SwitchProcessIdAsync(activeTrading));
                        
                        break;
                    case "FILLED":
                    case "NEW":
                    case "PARTIALLY_FILLED":
                        
                        logger.LogInformation($"Resuming #{{TradingId}}; Stage {{Stage}} :: " +
                            $"{nameof(TradeService.Step3UpdateOrderCreatedStageAsync)}", activeTrading.Id, activeTrading.Stage);
                        
                        await tradeService.Step3UpdateOrderCreatedStageAsync(await SwitchProcessIdAsync(activeTrading));
                        
                        break;
                    default:
                        
                        logger.LogError("Buy order (market) status unmapped: {Status}", buyOrder.Status);
                        
                        break;
                }
            }
            else
            {
                if(activeTrading.UpdatedAt?.AddSeconds(buyOrderCreatingTimeoutSeconds) < DateTimeOffset.Now)
                {
                    logger.LogWarning("Trading #{TradingId} :: discarding trade due requested to create buy order more than {OrderCreatingTimeoutSeconds} seconds ago but binance says that it not found.", activeTrading.Id, buyOrderCreatingTimeoutSeconds);

                    await dbAdapter.UpdateTradeCompletedAndNotInitializedStageAsync(activeTrading.Id,
                        abortReason: $"Requested to create buy order more than {buyOrderCreatingTimeoutSeconds} seconds ago but binance says that it not found.", await SwitchProcessIdAsync(activeTrading));
                }
                else
                {
                    logger.LogWarning(
                        $"Despite binance says that the BUY order not found, will skip for now in order to verify again in few seconds " +
                        $"to ensure the order was not created.", activeTrading.Id);
                }
            }
        }
    }
}
