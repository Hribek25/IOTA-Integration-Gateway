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

namespace IOTAGears.Services
{
    public class TimedBackgroundService : IHostedService, IDisposable    
    {
        private readonly Logger<TimedBackgroundService> _logger;
        private readonly NodeManager _nodemanager;
        private readonly DBManager _db;
        private Timer _timerHealthCheck;
        private Timer _timerPipelineTasks = null;
        private bool HealthCheckingInProgress = false;
        private bool ProcessingTasksInProgress = false;
        public bool ProcessingTasksActive { get; set; } = false;
        private readonly TimeSpan ProcessingTaskInterval = TimeSpan.FromSeconds(6); // task is processed every 6 seconds
        private readonly TimeSpan HealthCheckingTaskInterval = TimeSpan.FromSeconds(180);
        private readonly object balanceLock = new object();
        private bool disposed = false;

        public TimedBackgroundService(ILogger<TimedBackgroundService> logger, INodeManager nodemanager, IDBManager dbmanager)
        {
            _logger = (Logger<TimedBackgroundService>)logger;
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

            var tasks = _db.GetDBTaskEntryFromPipelineAsync().Result;

            if (tasks.Count>0)
            {
                bool ErrorOccurred = false;
                foreach (var item in tasks)
                {
                    if (item.Task == "SENDTX") // has to be all uppercase
                    {
                        var actualnode = this._nodemanager.SelectNode();
                        var actualPOWnode = this._nodemanager.SelectPOWNode();
                        // TODO: select also POW service and potential failover

                        if (actualPOWnode==null) //if no POW server then using the same node as for TIPS selection, etc.
                        {
                            actualPOWnode = actualnode;
                            if (actualPOWnode!=null)
                            {
                                _logger.LogInformation("No POW server. Using the standard one... Actual node: {actualnode}", actualnode);
                            }                            
                        }                        

                        if (actualnode != null && actualPOWnode !=null)
                        {
                            var IotaRepo = new RestIotaRepository(new RestClient(actualnode) { Timeout = 5000 }, new POWService(actualPOWnode));
                            var bundle = new Bundle();
                            Bundle RetBundle = null;
                            var guid = item.GlobalId;                            
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
                                _db.UpdateDBTaskEntryInPipeline(guid, 200, RetBundle.Hash.Value).Wait();                                
                            }
                        }
                        else
                        {
                            _logger.LogInformation("No nodes via which to send TX. Skipping it for the current cycle...");
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

            // general purpose nodes
            var Status = this._nodemanager.PerformHealthCheck(); //Performing health check
            var HealthyOnes = (from n in Status where n.Value == true select n.Key).ToList();
            this._nodemanager.Nodes = HealthyOnes;

            if (HealthyOnes.Count==0)
            {
                _logger.LogError("All nodes down!");
            }

            // POW nodes. Checking it via OPTIONS.
            Status = this._nodemanager.PerformPOWHealthCheck(); //Performing health check of POW nodes
            HealthyOnes = (from n in Status where n.Value == true select n.Key).ToList();
            this._nodemanager.POWNodes = HealthyOnes;

            if (HealthyOnes.Count == 0)
            {
                _logger.LogError("All POW nodes down!");
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                    // Dispose managed resources.
                    _timerHealthCheck?.Dispose();
                    _timerPipelineTasks.Dispose();
                }

                // Call the appropriate methods to clean up
                // unmanaged resources here.
                // If disposing is false,
                // only the following code is executed.


                // Note disposing has been done.
                disposed = true;
            }
        }

    }
}
