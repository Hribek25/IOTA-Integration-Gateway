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
using Swashbuckle.AspNetCore.Swagger;

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
            // singleton
            var dbcon = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = Program.DBLayerDataSource()}.ConnectionString
                );

            services.AddSingleton<IDBManager>(
                new DBManager(
                    dbcon,
                    LoggerFactory.CreateLogger<DBManager>()
                    )
                );

            // Register the Swagger generator
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc(
                    "v1",
                    new Info {
                        Title = "IOTA Gears API",
                        Version = "v1",
                        Description = "Integrate IOTA protocol with integration platforms such as ifttt.com or Office 365 to enable use cases based on workflows that are available today",
                        Contact = new Contact
                        {
                            Name = "GitHub Repo",
                            Email = string.Empty,
                            Url = "https://github.com/Hribek25/IOTA.Gears"
                        }
                    });
                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }


            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.), 
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "IOTA Gears API");
            });


            app.UseMvc();
        }
    }
}
