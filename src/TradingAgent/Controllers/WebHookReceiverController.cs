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

            var request = new Request(null);
            RequestProcessorBackgroundWorker.EnqueueRequest(request);
            
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

            // TODO: review
            if (!await dbAdapter.AnyActiveTradeAsync("BUSD"))
            {
                logger.LogInformation("Preparing to go trade!!!");
                TradeService.EnjBusdPrepared = true;
            }
            else
            {
                logger.LogInformation("There is activetranding, skipping....");
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
