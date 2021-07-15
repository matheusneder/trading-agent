using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TradingAgent
{
    public class RequestProcessorBackgroundWorker : BackgroundService
    {
        private readonly ILogger logger;
        private const int MainLoopDelayMilliseconds = 10;
        private static readonly ConcurrentQueue<Request> Requests = new ConcurrentQueue<Request>();
        private readonly List<Task> Jobs = new List<Task>();
        private readonly IRequestProcessor requestProcessor;
        private readonly IHostApplicationLifetime lifeTime;

        public static void EnqueueRequest(Request request)
        {
            Requests.Enqueue(request);
        }

        public RequestProcessorBackgroundWorker(ILoggerFactory loggerFactory, IRequestProcessor requestProcessor, IHostApplicationLifetime lifeTime)
        {
            logger = loggerFactory?.CreateLogger<RequestProcessorBackgroundWorker>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.requestProcessor = requestProcessor ?? throw new ArgumentNullException(nameof(requestProcessor));
            this.lifeTime = lifeTime ?? throw new ArgumentNullException(nameof(lifeTime));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Starting worker");
            
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogTrace("WorkerMainLoopAsync new iteration stating...");

                    foreach (Task job in Jobs.Where(i => i.IsCompleted).ToArray())
                    {
                        try
                        {
                            await job;
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Very unexpected exception was thrown while executing backgroud task");
                        }

                        Jobs.Remove(job);
                    }

                    if (Requests.TryDequeue(out Request request))
                    {
                        var task = await Task.Factory.StartNew(() => ProcessRequestAsync(request, stoppingToken),
                            TaskCreationOptions.LongRunning);
                        Jobs.Add(task);
                    }
                    else
                    {
                        logger.LogTrace("Nothing todo!");
                    }

                    await Task.Delay(MainLoopDelayMilliseconds);
                }

                logger.LogInformation("Terminating worker");
            }
            catch(Exception e)
            {
                logger.LogError(e, $"{nameof(RequestProcessorBackgroundWorker)}.ExecuteAsync main loop: Very unexpected exception");
                Environment.ExitCode = 1;
                lifeTime.StopApplication();
            }
        }

        private async Task ProcessRequestAsync(Request request, CancellationToken stoppingToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using (logger.BeginScope("New request {RequestId}", Guid.NewGuid()))
            {
                try
                {
                    await requestProcessor.ProcessAsync(request, stoppingToken);
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"{nameof(RequestProcessorBackgroundWorker)}.{nameof(ProcessRequestAsync)} very unexpected request ERROR!");
                }
            }
        }
    }
}
