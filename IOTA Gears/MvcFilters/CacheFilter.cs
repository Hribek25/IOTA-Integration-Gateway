using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IOTAGears.ActionFilters
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public class CacheTangleResponseAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;
        public int LifeSpan { get; set; } = 300;
        public int StatusCode { get; set; } = 200;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => new CacheFilter(logger: serviceProvider.GetService<ILoggerFactory>().CreateLogger<CacheFilter>())
        {
            DBManager = (Services.DBManager)serviceProvider.GetService<Services.IDBManager>(),
            CacheLifeSpan = LifeSpan,
            StatusCode = StatusCode
        };

        private class CacheFilter : IAsyncResourceFilter
        {
            public Services.DBManager DBManager { get; set; } = null;
            public int CacheLifeSpan { get; set; }
            public ILogger<CacheFilter> Logger { get; }
            public int StatusCode { get; set; } = 200;

            private void GetThreadInfo()
            {
                System.Threading.ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableAsyncIOThreads);
                Logger.LogDebug("Available AsyncIOThreads: {availableAsyncIOThreads}, Available Worker Threads: {availableWorkerThreads}", availableWorkerThreads, availableAsyncIOThreads);
            }

            public CacheFilter(ILogger<CacheFilter> logger)
            {
                Logger = logger;
                Logger.LogInformation("CACHE filter initiated");
            }

            public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
            {
                // do something before
                // Read response from cache
                var callerID = $"[{context.HttpContext.Request.Method}]{context.HttpContext.Request.Path}{context.HttpContext.Request.QueryString}";

                Logger.LogInformation("Cache request query: {context.HttpContext.Request.Path}", context.HttpContext.Request.Path);
                var c = await DBManager.GetCacheEntryAsync(
                    callerID,
                    "application/json", // TODO: move this to attribute?
                    CacheLifeSpan
                    );

                if (c!=null) // I have a response from cache, let's perform a shortcut
                {
                    context.Result = c;
                    Logger.LogInformation("Cache entry was loaded from cache for Request: {context.HttpContext.Request.Path}, Lifespan = {CacheLifeSpan}", context.HttpContext.Request.Path, CacheLifeSpan);
                    //GetThreadInfo();
                    return; 
                }
                else
                {
                    Logger.LogInformation("No cache entry found in cache for Request: {context.HttpContext.Request.Path}, Lifespan = {CacheLifeSpan}", context.HttpContext.Request.Path, CacheLifeSpan);

                    var resultContext = await next(); // let's execute a controller

                    if (c == null) // nothing was in cache and so storing results to cache
                    {
                        if (!resultContext.Canceled && resultContext.HttpContext.Response.StatusCode == StatusCode && resultContext.Result is Microsoft.AspNetCore.Mvc.JsonResult)
                        {
                            //only if JSON result and sucessfull call
                            // Write response to cache
                            await DBManager.AddCacheEntryAsync(
                                callerID, // request
                                (Microsoft.AspNetCore.Mvc.JsonResult)resultContext.Result, //result
                                resultContext.HttpContext.Response.ContentType //content type                                
                                );
                            Logger.LogInformation("New entry to CACHE for Request: {context.HttpContext.Request.Path}", context.HttpContext.Request.Path);
                        }
                    }
                }                
            }
        }

    }
        
}
