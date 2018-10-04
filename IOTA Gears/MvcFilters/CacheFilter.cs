using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IOTAGears.Services;

namespace IOTAGears.ActionFilters
{
    [AttributeUsage(AttributeTargets.All)]
    public class CacheTangleResponseAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;
        public int LifeSpan { get; set; } = 300;
        public int[] StatusCodes { get; set; } = { 200 };

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => new CacheFilter(
            logger: serviceProvider.GetService<ILoggerFactory>().CreateLogger<CacheFilter>(), // Logger service
            fsStorage: serviceProvider.GetService<IFsStorageManager>()) //FS Storage Service
        {            
            CacheLifeSpan = LifeSpan,
            StatusCodes = StatusCodes
        };

        private class CacheFilter : IAsyncResourceFilter
        {
            public FsStorageManager FsManager { get; }
            public Logger<CacheFilter> Logger { get; }

            public int CacheLifeSpan { get; set; }            
            public int[] StatusCodes { get; set; }
            
            public CacheFilter(ILogger<CacheFilter> logger, IFsStorageManager fsStorage)
            {
                Logger = (Logger<CacheFilter>)logger;
                FsManager = (FsStorageManager)fsStorage;

                Logger.LogInformation("CACHE filter initiated");
            }

            public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
            {
                // do something before
                // Read response from cache
                var callerID = $"[{context.HttpContext.Request.Method}]{context.HttpContext.Request.Path}{context.HttpContext.Request.QueryString}";

                Logger.LogInformation("Cache request query: {context.HttpContext.Request.Path}", context.HttpContext.Request.Path);
                var c = await FsManager.GetFSCacheEntryAsync(
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

                    var resultContext = await next(); // let's execute full controller - some API action to be executed

                    if (c == null) // nothing was in cache and so storing results to cache
                    {
                        if (!resultContext.Canceled && StatusCodes.Contains(resultContext.HttpContext.Response.StatusCode))
                        {
                            //only if JSON result and sucessfull call
                            // Write response to cache
                            await FsManager.AddFSCacheEntryAsync(
                                callerID, // request
                                resultContext.Result, //result
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
