global using System;
global using System.Linq;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Collections.Generic;
global using System.Threading.Tasks;
global using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace TestNSwag
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
