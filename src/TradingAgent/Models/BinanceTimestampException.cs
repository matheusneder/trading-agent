using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class BinanceTimestampException : Exception
    {
        public BinanceTimestampException()
        {

        }

        protected BinanceTimestampException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public override string Message => "Clock not sync between client and binance servers.";
    }
}
