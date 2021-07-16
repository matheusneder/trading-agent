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

        public WebHookReceiverController(ILoggerFactory loggerFactory, AppSecrets appSecrets)
        {
            logger = loggerFactory.CreateLogger<WebHookReceiverController>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.appSecrets = appSecrets ?? throw new ArgumentNullException(nameof(appSecrets));
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

        [HttpPost("/test-wh")]
        public IActionResult Test()
        {
            logger.LogInformation("Request received");
            return Ok();
        }
    }
}
