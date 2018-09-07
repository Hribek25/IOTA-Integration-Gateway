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
using Microsoft.Extensions.Hosting;

namespace IOTA_Gears
{
    public class Startup
    {        
        public Startup()
        {            
        }
               
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            // Node Manager
            services.AddSingleton<INodeManager, NodeManager>();
            
            // DB Manager
            services.AddTransient<IDBManager, DBManager>();

            // Tangle Repo
            services.AddTransient<ITangleRepository, TangleRepository>();

            // Register the Swagger generator
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc(
                    "v1",
                    new Info {
                        Title = "IOTA Integration Gateway",
                        Version = "v1",
                        Description = "Integrate IOTA protocol with business workflows that are available today",
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
                c.DescribeAllEnumsAsStrings();                
            });

            // Background Tasks Service
            services.AddSingleton<IHostedService, TimedBackgroundService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IHostingEnvironment env)
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
