using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradingAgent.Controllers
{
    [ApiController]
    public class WebHookReceiverController : ControllerBase
    {
        private readonly ILogger logger;
        private readonly AppSecrets appSecrets;
        private readonly DbAdapter dbAdapter;
        private static bool EnjBusdPrepared { get; set; } = false;

        public WebHookReceiverController(ILoggerFactory loggerFactory, AppSecrets appSecrets, DbAdapter dbAdapter)
        {
            logger = loggerFactory.CreateLogger<WebHookReceiverController>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.appSecrets = appSecrets ?? throw new ArgumentNullException(nameof(appSecrets));
            this.dbAdapter = dbAdapter ?? throw new ArgumentNullException(nameof(dbAdapter));
        }

        [HttpPost("/events/{whrObfuscatedRouteSegment}/ENJBUSD")]
        public IActionResult TradeEnjBusd(string whrObfuscatedRouteSegment)
        {
            if(whrObfuscatedRouteSegment != appSecrets.WhrObfuscatedRouteSegment)
            {
                return BadRequest(string.Empty);
            }

            logger.LogInformation($"New request {nameof(TradeEnjBusd)}");

            if (EnjBusdPrepared)
            {
                EnjBusdPrepared = false;
                var request = new Request(null);
                RequestProcessorBackgroundWorker.EnqueueRequest(request);
            } 
            else
            {
                logger.LogInformation("Skipping, not prepared to start a new trade yet.");
            }
            
            return Ok();
        }

        [HttpPost("/events/{whrObfuscatedRouteSegment}/ENJBUSD/Prepare")]
        public async Task<IActionResult> TradeEnjBusdPrepareAsync(string whrObfuscatedRouteSegment)
        {
            if (whrObfuscatedRouteSegment != appSecrets.WhrObfuscatedRouteSegment)
            {
                return BadRequest(string.Empty);
            }

            logger.LogInformation($"New request {nameof(TradeEnjBusdPrepareAsync)}");

            if (!await dbAdapter.AnyActiveTradeAsync("BUSD"))
            {
                EnjBusdPrepared = true;
                logger.LogInformation("Prepared to start a new trade!");
            }
            else
            {
                logger.LogInformation("Skipping, currently already trading.");
            }

            return Ok();
        }

        [HttpPost("/test-wh")]
        public IActionResult Test()
        {
            logger.LogInformation("Request received");
            return Ok();
        }
    }
}
