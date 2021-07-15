using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class BinanceSignatureException : Exception
    {
        public BinanceSignatureException()
        {

        }

        protected BinanceSignatureException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public override string Message => "Invalid signature.";
    }
}
