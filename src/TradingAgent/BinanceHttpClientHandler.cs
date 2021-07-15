using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TradingAgent
{
    public class BinanceHttpClientHandler : HttpClientHandler
    {
        private readonly string binanceApiKey;
        private readonly string binanceApiSecret;

        public BinanceHttpClientHandler(string binanceApiKey, string binanceApiSecret)
        {
            this.binanceApiKey = binanceApiKey;
            this.binanceApiSecret = binanceApiSecret;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri;
            var query = new List<string>(
                 Regex.Replace(requestUri.Query, "^\\?", string.Empty).Split('&').Where(s => !string.IsNullOrWhiteSpace(s)));

            query.Add($"timestamp={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

            var signature = BitConverter.ToString(
                    new HMACSHA256(Encoding.ASCII.GetBytes(binanceApiSecret))
                        .ComputeHash(Encoding.ASCII.GetBytes(string.Join('&', query))))
                        .Replace("-", string.Empty)
                        .ToLower();

            query.Add($"signature={signature}");

            var uriBuilder = new UriBuilder(requestUri);
            uriBuilder.Query = string.Join('&', query);
            request.RequestUri = uriBuilder.Uri;
            request.Headers.Add("X-MBX-APIKEY", binanceApiKey);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
