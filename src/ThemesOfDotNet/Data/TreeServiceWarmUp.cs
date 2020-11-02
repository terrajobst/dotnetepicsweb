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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _treeService.InvalidateAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
