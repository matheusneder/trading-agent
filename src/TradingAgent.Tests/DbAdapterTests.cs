using System;
using System.Threading.Tasks;
using TradingAgent.Models;
using Xunit;

namespace TradingAgent.Tests
{
    public class DbAdapterTests
    {
        [Fact]
        public async Task InsertNewOperationAsync_ThereIsAnotherTradingInExecution_Error()
        {
            var dbAdapter = new DbAdapter();
            var ex = await Assert.ThrowsAsync<TradingException>(async () => await dbAdapter.InsertNewOperationAsync("TCOINH", "TCOINT", 12));
            Assert.Equal(TradingError.ThereIsAnotherTradingInExecution, ex.TradingError);
        }
    }
}
