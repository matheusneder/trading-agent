using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TradingAgent.Tests
{
    public class BinanceApiAdapterTests
    {
        [Fact]
        public async Task GetAccountInformation_Success()
        {
            var appSecrets = AppSecrets.ReadFromFile();
            var binanceApiAdapter = new BinanceApiAdapter(Startup.CreateBinancePrivateApiClient(appSecrets), Startup.CreateBinancePublicApiClient(appSecrets));
            var accountInfo = await binanceApiAdapter.GetAccountInformationAsync();
            Assert.Contains(accountInfo.Balances, m => m.Asset == "BTC");
        }
    }
}
