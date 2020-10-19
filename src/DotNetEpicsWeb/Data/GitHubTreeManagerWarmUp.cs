using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace DotNetEpicsWeb.Data
{
    internal sealed class GitHubTreeManagerWarmUp : IHostedService
    {
        private readonly GitHubTreeManager _treeManager;

        public GitHubTreeManagerWarmUp(GitHubTreeManager treeManager)
        {
            _treeManager = treeManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _treeManager.InvalidateAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
