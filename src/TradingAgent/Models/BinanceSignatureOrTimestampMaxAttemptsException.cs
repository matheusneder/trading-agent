using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class BinanceSignatureOrTimestampMaxAttemptsException : Exception
    {
        public BinanceSignatureOrTimestampMaxAttemptsException(Exception e)
            : base($"BinanceSignatureOrTimestampMaxAttemptsException :: {e.GetType().Name} :: {e.Message}", e)
        {

        }

        protected BinanceSignatureOrTimestampMaxAttemptsException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
