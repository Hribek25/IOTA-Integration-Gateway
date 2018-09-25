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
using IOTAGears.Services;
using Microsoft.Data.Sqlite;
using Swashbuckle.AspNetCore.Swagger;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Data.HashFunction.xxHash;

namespace IOTAGears
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

            // xxHash service            
            services.AddSingleton(xxHashFactory.Instance.Create());

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
                        Description = "Let's integrate IOTA protocol with business workflows that are available today!",                        
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

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder => builder
                                .AllowAnyOrigin()
                                .AllowAnyMethod()
                    );
            });
            
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost ;
            });
            
        }

#pragma warning disable CA1822 // Member Configure does not access instance data and can be marked as static
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IHostingEnvironment env)
        {

            app.UseCors("AllowAllOrigins");
            app.UseForwardedHeaders(); // I assume it is placed behind the proxy


            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.), 
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint(Program.SwaggerJsonFile(), "IOTA Gateway API");
                c.RoutePrefix = "docs";
                c.DocumentTitle = "IOTA Gateway API Documentation";
                c.DocExpansion(DocExpansion.None);
            });            

            app.UseMvc();
        }
#pragma warning restore CA1822 // Member Configure does not access instance data and can be marked as static
    }
}
