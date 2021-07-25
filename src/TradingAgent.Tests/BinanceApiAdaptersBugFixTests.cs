using Moq;
using Newtonsoft.Json;
using Refit;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TradingAgent.Models;
using Xunit;

namespace TradingAgent.Tests
{
    public class BinanceApiAdaptersBugFixTests
    {
        private readonly BinanceApiAdapter binanceApiAdapter;
        private readonly Mock<IBinancePrivateApiClient> binancePrivateApiClientMock = new Mock<IBinancePrivateApiClient>();
        private readonly Mock<IBinancePublicApiClient> binancePublicApiCLientMock = new Mock<IBinancePublicApiClient>();

        public BinanceApiAdaptersBugFixTests()
        {
            binanceApiAdapter = new BinanceApiAdapter(binancePrivateApiClientMock.Object, binancePublicApiCLientMock.Object);
        }

        [Fact]
        public async Task QueryOrderNotFound_Success()
        {
            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri("https://dummy.com")
            };

            var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = httpRequestMessage,
                Content = new StringContent(JsonConvert.SerializeObject(new BinanceErrorDto()
                {
                    code = -2013,
                    msg = "Not found!"
                }), Encoding.UTF8, "application/json")
            };

            var apiException = await ApiException.Create(
                    httpRequestMessage,
                    HttpMethod.Post,
                    httpResponseMessage,
                    new RefitSettings());

            binancePrivateApiClientMock.Setup(m => m.QueryOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(apiException);

            Assert.Null(await binanceApiAdapter.GetOrderAsync(1, "BUSD", "ENJ", OrderKind.BuyMarketOrder));
        }

        [Fact]
        public async Task QueryOcoNotFound_Success()
        {
            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri("https://dummy.com")
            };

            var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = httpRequestMessage,
                Content = new StringContent(JsonConvert.SerializeObject(new BinanceErrorDto()
                {
                    code = -2018,
                    msg = "Not found!"
                }), Encoding.UTF8, "application/json")
            };

            var apiException = await ApiException.Create(
                    httpRequestMessage,
                    HttpMethod.Post,
                    httpResponseMessage,
                    new RefitSettings());

            binancePrivateApiClientMock.Setup(m => m.QueryOcoAsync(It.IsAny<string>()))
                .ThrowsAsync(apiException);

            Assert.Null(await binanceApiAdapter.GetOcoOrderStatusAsync(1, "xpto"));
        }
    }
}
