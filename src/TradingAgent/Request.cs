using System.IO.Pipes;

namespace TradingAgent
{
    public class Request
    {
        private readonly object data;

        public Request(object data)
        {
            this.data = data;
        }
    }
}