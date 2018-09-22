using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IOTAGears
{
    public enum DbLayerProvider
    {
        Sqlite, // Default should be Sqlite
        Mysql
    }


    public static class Program
    {
        public static string SqliteDbLayerDataSource() => 
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "iotagears_pipeline.sqlite"
                );

        public static string AppVersion() =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public static string SwaggerJsonFile() => "/swagger/v1/swagger.json";

        public static void Main(string[] args)
        {
            var wh = BuildWebHost(args); // preparing web host + service container

            // Read a configuration before runnning webhost
            var conf = (IConfiguration)wh.Services.GetService(typeof(IConfiguration));            
            var DbProvider = conf.GetValue<DbLayerProvider>("DBLayerProvider");
            var DbConnStr = conf.GetValue<string>("SqlDbConnStr"); 

            if (DbLayer.IsDBLayerReady(DbConnStr, DbProvider))
            {
                Console.WriteLine("DB layer is ready. Program/Main executes...");
                wh.Run();                
            }
            else
            {
                Console.WriteLine("DB layer is not ready. Halting...");
                Console.WriteLine("Press any key to close");
                Console.ReadKey();
            }

            Console.WriteLine("Program has been terminated...");
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseApplicationInsights()
                .UseStartup<Startup>()
                .Build();                
    }
}
