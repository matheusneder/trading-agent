using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingAgent
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(serverOptions =>
                    {
                        serverOptions.AddServerHeader = false; 
                        serverOptions.Listen(IPAddress.Any, AppConfig.ReadFromFile().HttpsPort,
                            listenOptions =>
                            {
                                listenOptions.UseHttps("mytvwhr.ddns.net.pfx",
                                    AppSecrets.ReadFromFile().PfxPassword);
                            });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}
