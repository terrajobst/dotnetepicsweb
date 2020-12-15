using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace ThemesOfDotNet
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using var host =
                Host.CreateDefaultBuilder(args)
                    .ConfigureWebHostDefaults(
                        webBuilder => webBuilder.UseStartup<Startup>())
                    .Build();

            await host.RunAsync();
        }
    }
}
