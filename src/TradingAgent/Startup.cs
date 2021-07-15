using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Refit;

namespace TradingAgent
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var appConfig = AppConfig.ReadFromFile();
            var appSecrets = AppSecrets.ReadFromFile();
            services.AddSingleton(appSecrets);
            services.AddSingleton(appConfig);
            services.AddSingleton<TradeService>();
            services.AddSingleton<DbAdapter>();
            services.AddSingleton<BinanceApiAdapter>();
            services.AddHostedService<RequestProcessorBackgroundWorker>();
            services.AddHostedService<RetrierBackgroudWorker>();
            services.AddSingleton<IRequestProcessor, TradingRequestProcessor>();
            services.AddSingleton(CreateBinancePrivateApiClient(appSecrets));
            services.AddSingleton(CreateBinancePublicApiClient(appSecrets));
            services.AddControllers();
        }

        public static IBinancePrivateApiClient CreateBinancePrivateApiClient(AppSecrets appSecrets)
        {
            return RestService.For<IBinancePrivateApiClient>(
                new HttpClient(new BinanceHttpClientHandler(appSecrets.BinanceApiKey, appSecrets.BinanceApiSecret)
                {
                    Proxy = new WebProxy()
                    {
                        Address = new Uri("http://localhost:18888")
                    }
                })
                {
                    BaseAddress = new Uri(appSecrets.BinanceBaseAddress)
                });
        }

        public static IBinancePublicApiClient CreateBinancePublicApiClient(AppSecrets appSecrets)
        {
            return RestService.For<IBinancePublicApiClient>(
                new HttpClient()
                {
                    BaseAddress = new Uri(appSecrets.BinanceBaseAddress)
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
