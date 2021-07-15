using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace TradingAgent.Models
{
    [Serializable]
    public class TradingException : Exception
    {
        public TradingError TradingError { get; }

        public TradingException(TradingError tradingError)
        {
            TradingError = tradingError;
        }

        protected TradingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
