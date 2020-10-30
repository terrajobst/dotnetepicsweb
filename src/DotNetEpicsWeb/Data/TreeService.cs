using System;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetEpicsWeb.Data
{
    public sealed class TreeService
    {
        private readonly GitHubTreeProvider _githubTreeProvider;
        private readonly AzureDevOpsTreeProvider _azureTreeProvider;

        private Tree _tree;

        public TreeService(GitHubTreeProvider gitHubTreeProvider, AzureDevOpsTreeProvider azureTreeProvider)
        {
            _githubTreeProvider = gitHubTreeProvider;
            _azureTreeProvider = azureTreeProvider;
        }

        public Tree Tree => _tree;

        public async Task InvalidateAsync()
        {
            var gitHubTreeTask = _githubTreeProvider.GetTreeAsync();
            var azureTreeTask = _azureTreeProvider.GetTreeAsync();
            await Task.WhenAll(gitHubTreeTask, azureTreeTask);

            _tree = MergeTrees(gitHubTreeTask.Result, azureTreeTask.Result);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private Tree MergeTrees(Tree result1, Tree result2)
        {
            return new Tree(result1.Roots.Concat(result2.Roots));
        }

        public event EventHandler Changed;
    }
}
