using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class BinanceLimitsException : Exception
    {
        private readonly HttpStatusCode statusCode;
        private readonly string content;

        public BinanceLimitsException(HttpStatusCode statusCode, string content)
        {
            this.statusCode = statusCode;
            this.content = content;
        }

        public override string Message => $"HttpStatusCode: {statusCode}; Contet: {content}";

        protected BinanceLimitsException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
