using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tangle.Net.Entity;
using Tangle.Net.ProofOfWork.Service;
using Tangle.Net.Repository;
using Tangle.Net.Utils;

namespace IOTA_Gears.Services
{
    internal class TimedBackgroundService : IHostedService, IDisposable    
    {
        private readonly ILogger _logger;
        private readonly NodeManager _nodemanager;
        private readonly DBManager _db;
        private Timer _timerHealthCheck;
        private Timer _timerPipelineTasks = null;
        private bool HealthCheckingInProgress = false;
        private bool ProcessingTasksInProgress = false;
        public bool ProcessingTasksActive = false;
        private readonly TimeSpan ProcessingTaskInterval = TimeSpan.FromSeconds(15);
        private readonly TimeSpan HealthCheckingTaskInterval = TimeSpan.FromSeconds(180);
        private readonly object balanceLock = new object();
        
        public TimedBackgroundService(ILogger<TimedBackgroundService> logger, INodeManager nodemanager, IDBManager dbmanager)
        {
            _logger = logger;
            _nodemanager = (NodeManager)nodemanager;
            _db = (DBManager)dbmanager;
            _logger.LogInformation("Background Service initialized.");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Service is starting.");
            _timerHealthCheck = new Timer(DoNodesHealthCheck, null, TimeSpan.Zero, HealthCheckingTaskInterval);
            StartProcessingPipeline();

            return Task.CompletedTask;
        }

        private void DoProcessPipelineTasks(object state)
        {
            const int maxMessageChars = 2187;
            if (this.ProcessingTasksInProgress)
            {
                return; // task is already running, exiting
            }
            this.ProcessingTasksInProgress = true; // semaphore

            _logger.LogInformation("Background Task: Processing Tasks... Starting");

            var tasks = _db.GetTaskEntryFromPipelineAsync().Result;

            if (tasks.Count>0)
            {
                bool ErrorOccurred = false;
                foreach (var item in tasks)
                {
                    if (item.Task == "SendTX")
                    {
                        var actualnode = this._nodemanager.SelectNode();
                        // TODO: select also POW service and potential failover
                        if (actualnode != null)
                        {
                            var IotaRepo = new RestIotaRepository(new RestClient(actualnode) { Timeout = 5000 }, new PoWSrvService());
                            var bundle = new Bundle();
                            Bundle RetBundle = null;
                            var guid = item.GuId;
                            var rowid = item.Rowid;
                            var seed = Seed.Random();
                            var input = item.Input;

                            var MessageinTrytes = TryteString.FromUtf8String(input.Message).Value; // converting to trytes

                            while (MessageinTrytes.Length > 0) // now let's split message to several transactions in case it is too long. Max is 2187 trytes
                            {
                                bundle.AddTransfer(
                                new Transfer
                                {
                                    Address = new Address(input.Address),
                                    Tag = new Tag(input.Tag),
                                    ValueToTransfer = 0,
                                    Timestamp = Timestamp.UnixSecondsTimestamp,
                                    Message = new TryteString(MessageinTrytes.Length > maxMessageChars ? MessageinTrytes.Substring(0, maxMessageChars) : MessageinTrytes)
                                });
                                MessageinTrytes = MessageinTrytes.Length > maxMessageChars ? MessageinTrytes.Substring(maxMessageChars - 1) : "";
                            }
                                                        
                            try
                            {
                                _logger.LogInformation("Performing external API call SendTransfer via {actualnode}", actualnode);
                                RetBundle = IotaRepo.SendTransfer(seed, bundle, 2);
                                _logger.LogInformation("Background Task: Processing Tasks... Transaction sent, Bundle Hash: {RetBundle.Hash.Value}", RetBundle.Hash.Value);
                            }
                            catch (Exception e)
                            {
                                // swallowing exception
                                ErrorOccurred = true;
                                _logger.LogInformation("Background Task: Processing Tasks... Error occured, Error: {e}", e);
                            }

                            if (!ErrorOccurred)
                            {
                                _db.UpdateTaskEntryInPipeline(rowid, 200, RetBundle.Hash.Value).Wait();                                
                            }
                        }
                    }
                }
            }
            else
            { // no other tasks in pipeline
                StopProcessingPipeline();
            }            
            this.ProcessingTasksInProgress = false; // semaphore
            _logger.LogInformation("Background Task: Processing Tasks... Ending");
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

            var Status = this._nodemanager.PerformHealthCheck(); //Performing health check
            var HealthyOnes = (from n in Status where n.Value == true select n.Key).ToList();
            this._nodemanager.Nodes = HealthyOnes;

            if (HealthyOnes.Count==0)
            {
                _logger.LogError("All nodes down!");
            }

            _logger.LogInformation("Background Task: Health Check of nodes... Ending");
            this.HealthCheckingInProgress = false; // basic semaphore
        }

        public void StartProcessingPipeline()
        {
            lock (balanceLock)
            {
                if (_timerPipelineTasks == null)
                {
                    _timerPipelineTasks = new Timer(DoProcessPipelineTasks, null, TimeSpan.FromSeconds(10), ProcessingTaskInterval);
                    ProcessingTasksActive = true;
                    _logger.LogInformation("Background Task: Pipeline Tasks Processor... starting");
                }
                else
                {
                    if (!ProcessingTasksActive)
                    {
                        _timerPipelineTasks.Change(TimeSpan.Zero, ProcessingTaskInterval);
                        ProcessingTasksActive = true;
                        _logger.LogInformation("Background Task: Pipeline Tasks Processor... starting");
                    }
                }
            } 
        }

        public void StopProcessingPipeline()
        {
            lock (balanceLock)
            {
                if (ProcessingTasksActive)
                {
                    _timerPipelineTasks?.Change(Timeout.Infinite, 0);
                    ProcessingTasksActive = false;
                    _logger.LogInformation("Background Task: Pipeline Tasks Processor... stopping");
                }
            }                        
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Service is stopping.");
            _timerHealthCheck?.Change(Timeout.Infinite, 0);
            StopProcessingPipeline();

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timerHealthCheck?.Dispose();
            _timerPipelineTasks.Dispose();
        }
    }
}
