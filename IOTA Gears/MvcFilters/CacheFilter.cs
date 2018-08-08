using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace IOTA_Gears.ActionFilters
{
    public class CacheTangleResponseAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;
        public int LifeSpan = 300;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            return new CacheFilter() {
                DBManager = (Services.DBManager)serviceProvider.GetService<Services.IDBManager>(),
                CacheLifeSpan = LifeSpan
            };
        }

        public class CacheFilter : IAsyncResourceFilter
        {
            public Services.DBManager DBManager { get; set; } = null;
            public int CacheLifeSpan { get; set; }

            public CacheFilter()
            {
                Console.WriteLine("CACHE filter initiated");
            }

            public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
            {
                //var db = context.HttpContext.RequestServices.GetService<Services.IDBManager>();
                
                // do something before
                // TODO: reading from cache
                Console.WriteLine($"Cache request call: {context.HttpContext.Request.Path}");
                var c = await DBManager.GetCacheEntryAsync(
                    context.HttpContext.Request.Path,
                    "application/json", //TODO: move this to attribute?
                    CacheLifeSpan
                    );

                if (c!=null) // I have a response from cache, let's perform a shortcut
                {
                    context.Result = c;
                    //var tsk = await Task.FromResult<object>(null); // TODO: Do I need to do this because of async?
                    return; 
                }
                else
                {
                    var resultContext = await next(); // let's execute a controller

                    if (c == null) // nothing was in cache and so saving result to a cache
                    {
                        if (!resultContext.Canceled & resultContext.HttpContext.Response.StatusCode == 200 & resultContext.Result is Microsoft.AspNetCore.Mvc.JsonResult)
                        {
                            //only if JSON result and sucessfull call
                            await DBManager.AddCacheEntryAsync(
                                context.HttpContext.Request.Path, // request
                                (Microsoft.AspNetCore.Mvc.JsonResult)resultContext.Result, //result
                                resultContext.HttpContext.Response.ContentType //content type                                
                                );
                        }
                    }
                }                
            }
        }

    }
        
}
