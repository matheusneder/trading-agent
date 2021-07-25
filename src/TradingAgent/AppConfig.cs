using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TradingAgent
{
    public class AppConfig
    {
        public decimal TargetProfitPerTradePercent { get; set; } = -1;
        public decimal StopLossPercent { get; set; } = -1;
        public decimal RollbackPricePercent { get; set; } = -1;
        public decimal UpgradePricePercent { get; set; } = -1;
        public decimal EstimatedFeesPercent { get; set; } = -1;
        public string HoldAsset { get; set; }
        public string TradeAsset { get; set; }
        public int HttpsPort { get; set; } = 5001;
        public decimal HoldAssetToTradePercent { get; set; } = 100;

        public static AppConfig ReadFromFile()
        {
            string jsonText;

            try
            {
                jsonText = File.ReadAllText("AppConfig.json");
            }
            catch (IOException e)
            {
                throw new InvalidOperationException("Could not read AppConfig.json", e);
            }

            var result = JsonSerializer.Deserialize<AppConfig>(jsonText);

            if (string.IsNullOrEmpty(result.HoldAsset) || string.IsNullOrEmpty(result.TradeAsset) || 
                result.TargetProfitPerTradePercent == -1 || result.StopLossPercent == -1 || 
                result.RollbackPricePercent == -1 || result.EstimatedFeesPercent == -1 || result.UpgradePricePercent == -1)
            {
                throw new InvalidOperationException("Missing configuration");
            }

            return result;
        }
    }
}
