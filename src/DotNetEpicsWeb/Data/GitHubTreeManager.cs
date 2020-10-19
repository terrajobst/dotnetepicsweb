using System;
using System.Threading.Tasks;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubTreeManager
    {
        private readonly IGitHubTreeService _treeService;
        private GitHubIssueTree _tree;

        public GitHubTreeManager(IGitHubTreeService treeService)
        {
            _treeService = treeService;
        }

        public GitHubIssueTree Tree => _tree;

        public async Task InvalidateAsync()
        {
            _tree = await _treeService.GetIssueTreeAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Changed;
    }
}
