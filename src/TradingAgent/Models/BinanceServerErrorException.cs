using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class BinanceServerErrorException : Exception
    {
        private readonly HttpStatusCode statusCode;
        private readonly string content;

        public BinanceServerErrorException(HttpStatusCode statusCode, string content)
        {
            this.statusCode = statusCode;
            this.content = content;
        }

        public override string Message => $"HttpStatusCode: {statusCode}; Contet: {content}";

        protected BinanceServerErrorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
