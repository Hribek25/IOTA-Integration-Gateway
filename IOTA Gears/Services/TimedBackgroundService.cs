using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IOTA_Gears.Services
{
    internal class TimedBackgroundService : IHostedService, IDisposable    
    {
        private readonly ILogger _logger;
        private readonly NodeManager _nodemanager;
        private Timer _timerHealthCheck;
        private bool HealthCheckingInProgress = false;

        public TimedBackgroundService(ILogger<TimedBackgroundService> logger, INodeManager nodemanager)
        {
            _logger = logger;
            _nodemanager = (NodeManager)nodemanager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Service is starting.");
            _timerHealthCheck = new Timer(DoNodesHealthCheck, null, TimeSpan.Zero, TimeSpan.FromSeconds(180));

            return Task.CompletedTask;
        }

        private void DoNodesHealthCheck(object state)
        {
            if (this.HealthCheckingInProgress)
            {
                _logger.LogInformation("Background Task: Health Check of nodes... Cancelled due to another runnning task of the same type");
                return;
            }
            this.HealthCheckingInProgress = true; // basic semaphore
            _logger.LogInformation("Background Task: Health Check of nodes... Starting");

            var Status = this._nodemanager.PerformHealthCheckAsync().Result; //Performing health check
            var HealthyOnes = (from n in Status where n.Value == true select n.Key).ToList();
            this._nodemanager.Nodes = HealthyOnes;

            if (HealthyOnes.Count==0)
            {
                _logger.LogError("All nodes down!");
            }

            _logger.LogInformation("Background Task: Health Check of nodes... Ending");
            this.HealthCheckingInProgress = false; // basic semaphore
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Service is stopping.");
            _timerHealthCheck?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timerHealthCheck?.Dispose();
        }
    }
}
