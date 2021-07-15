using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public class TradingRequestProcessor : IRequestProcessor
    {
        private readonly ILogger logger;
        private readonly TradeService tradeService;

        public TradingRequestProcessor(ILoggerFactory loggerFactory, TradeService tradeService)
        {
            logger = loggerFactory?.CreateLogger<RequestProcessorBackgroundWorker>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));
        }

        public async Task ProcessAsync(Request request, CancellationToken stoppingToken)
        {
            try
            {
                await tradeService.Step1CheckConditionsThenRegisterANewTradeAsync();
            }
            catch(Exception e)
            {
                logger.LogError(e, $"{nameof(ProcessAsync)} Error: {e.Message}");
            }
        }
    }
}
