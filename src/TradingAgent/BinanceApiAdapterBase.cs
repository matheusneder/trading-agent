using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TradingAgent.Models;

namespace TradingAgent
{
    public abstract class BinanceApiAdapterBase
    {
        protected async Task HandleErrorsAsync(ApiException e, Func<int, bool> errorHandler = null)
        {
            CheckException(e);

            if (IsClientError(e.StatusCode))
            {
                var errorDto = await e.GetContentAsAsync<BinanceErrorDto>();
                CheckCommonErrorCodes(errorDto.code);

                if (errorHandler != null && errorHandler.Invoke(errorDto.code))
                {
                    return; // error was handled
                }
            }

            throw new BinanceUnknowErrorException(e.StatusCode, e.Content);
        }

        private void CheckCommonErrorCodes(int code)
        {
            switch (code)
            {
                case -1021:
                    throw new BinanceTimestampException();
                case -1022:
                    throw new BinanceSignatureException();
            }
        }

        private bool IsClientError(HttpStatusCode statusCode) => (int)statusCode >= 400 && (int)statusCode <= 499;

        private void CheckException(ApiException e)
        {
            switch (e.StatusCode)
            {
                case HttpStatusCode.Forbidden:
                case ((HttpStatusCode)429):
                case ((HttpStatusCode)418):
                    throw new BinanceLimitsException(e.StatusCode, e.Content);
            }

            if (e.StatusCode >= HttpStatusCode.InternalServerError)
            {
                throw new BinanceServerErrorException(e.StatusCode, e.Content);
            }
        }
    }
}
