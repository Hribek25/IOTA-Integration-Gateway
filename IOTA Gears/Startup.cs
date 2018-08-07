using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IOTA_Gears.Services;
using Microsoft.Data.Sqlite;

namespace IOTA_Gears
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        private IHostingEnvironment HostingEnv { get;  }
        private ILoggerFactory LoggerFactory { get; }

        public Startup(IConfiguration configuration, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            HostingEnv = env;
            LoggerFactory = loggerFactory;                       
        }
               
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            // Node Manager
            // singleton - initialized only once using values from json config file
            var nm = new NodeManager((from i in Configuration.AsEnumerable()
                                      where i.Key.StartsWith("IOTANodes:")
                                      select i.Value).ToList<string>(),
                                      LoggerFactory.CreateLogger<NodeManager>()
                                     );
            services.AddSingleton<INodeManager>(nm);

            // Tangle Repo
            // this is not a singleton in order to change public nodes per each request
            services.AddTransient<ITangleRepository>(
                s => new TangleRepository(
                    nm,
                    LoggerFactory.CreateLogger<TangleRepository>()
                    )
                );  // incl reference to node manager


            // DB Manager
            var dbcon = new SqliteConnectionStringBuilder
            {
                DataSource = System.IO.Path.Combine(HostingEnv.ContentRootPath, "pipeline.sqlite")
            };
            services.AddSingleton<IDBManager>(
                new DBManager(
                    dbcon,
                    LoggerFactory.CreateLogger<DBManager>()
                    )
                );
                        
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
                   
            app.UseMvc();
        }
    }
}
