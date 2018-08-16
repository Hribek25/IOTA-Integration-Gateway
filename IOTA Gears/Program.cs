﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IOTA_Gears
{
    public class Program
    {
        public static string DBLayerDataSource() => 
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "iotagears_pipeline.sqlite"
                );

        public static void Main(string[] args)
        {
            if (DbLayer.IsDBLayerReady())
            {
                Console.WriteLine("DB layer is ready. Program/Main executes...");
                BuildWebHost(args).Run();
            }
            else
            {
                Console.WriteLine("DB layer is not ready. Halting...");
            }            
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .Build();
                
    }
}