using System.Threading;
using System.Threading.Tasks;

namespace TradingAgent
{
    public interface IRequestProcessor
    {
        Task ProcessAsync(Request request, CancellationToken stoppingToken);
    }
}