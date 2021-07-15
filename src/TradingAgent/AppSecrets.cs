using System;
using System.IO;
using System.Text.Json;

namespace TradingAgent
{
    public class AppSecrets
    {
        public string BinanceApiKey { get; set; }
        public string BinanceApiSecret { get; set; }
        public string PfxPassword { get; set; }
        public string WhrObfuscatedRouteSegment { get; set; }
        public string SqlConnectionString { get; set; }
        public string BinanceBaseAddress { get; set; }

        public static AppSecrets ReadFromFile()
        {
            string jsonText;

            try
            {
                jsonText = File.ReadAllText("AppSecrets.json");
            }
            catch(IOException e)
            {
                throw new InvalidOperationException("Could not read AppSecrets.json", e);
            }

            var result = JsonSerializer.Deserialize<AppSecrets>(jsonText);

            if(string.IsNullOrEmpty(result.BinanceApiKey) || string.IsNullOrEmpty(result.BinanceApiSecret) || string.IsNullOrEmpty(result.BinanceBaseAddress)
                || string.IsNullOrEmpty(result.PfxPassword) || string.IsNullOrEmpty(result.WhrObfuscatedRouteSegment) || string.IsNullOrEmpty(result.SqlConnectionString))
            {
                throw new InvalidOperationException("Could not read BinanceApiKey/BinanceApiSecret/PfxPassword/WhrObfuscatedRouteSegment/SqlConnectionString");
            }

            return result;
        }
    }
}