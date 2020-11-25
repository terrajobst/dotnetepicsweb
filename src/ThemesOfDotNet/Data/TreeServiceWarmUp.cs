using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace ThemesOfDotNet.Data
{
    internal sealed class TreeServiceWarmUp : IHostedService
    {
        private readonly TreeService _treeService;

        public TreeServiceWarmUp(TreeService treeService)
        {
            _treeService = treeService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Don't block the start up
            _ = _treeService.InvalidateAsync();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
