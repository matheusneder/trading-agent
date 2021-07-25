using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class BinanceUnknowErrorException : Exception
    {
        private readonly HttpStatusCode statusCode;
        private readonly string content;

        public BinanceUnknowErrorException(HttpStatusCode statusCode, string content)
        {
            this.statusCode = statusCode;
            this.content = content;
        }

        public override string Message => $"{nameof(BinanceUnknowErrorException)} :: HttpStatusCode: {statusCode}; Content: {content}";

        protected BinanceUnknowErrorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
