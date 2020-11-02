using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace ThemesOfDotNet.Data
{
    public sealed class TreeService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly GitHubTreeProvider _githubTreeProvider;
        private readonly AzureDevOpsTreeProvider _azureTreeProvider;

        private Tree _tree;

        public TreeService(IWebHostEnvironment environment, GitHubTreeProvider gitHubTreeProvider, AzureDevOpsTreeProvider azureTreeProvider)
        {
            _environment = environment;
            _githubTreeProvider = gitHubTreeProvider;
            _azureTreeProvider = azureTreeProvider;
        }

        public Tree Tree => _tree;

        public async Task InvalidateAsync()
        {
            _tree = await LoadTree();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private async Task<Tree> LoadTree()
        {
            if (!_environment.IsDevelopment())
            {
                return await LoadTreeFromProvidersAsync();
            }
            else
            {
                var tree = await LoadTreeFromCacheAsync();
                if (tree == null)
                    tree = await LoadTreeFromProvidersAsync();
                await SaveTreeToCacheAsync(tree);
                return tree;
            }
        }

        private async Task<Tree> LoadTreeFromProvidersAsync()
        {
            var gitHubTreeTask = _githubTreeProvider.GetTreeAsync();
            var azureTreeTask = _azureTreeProvider.GetTreeAsync();
            await Task.WhenAll(gitHubTreeTask, azureTreeTask);
            return MergeTrees(gitHubTreeTask.Result, azureTreeTask.Result);
        }

        private async Task<Tree> LoadTreeFromCacheAsync()
        {
            var fileName = GetCacheFileName();
            if (!File.Exists(fileName))
                return null;

            using var stream = File.OpenRead(fileName);
            return await JsonSerializer.DeserializeAsync<Tree>(stream);
        }

        private async Task SaveTreeToCacheAsync(Tree tree)
        {
            var fileName = GetCacheFileName();
            using var stream = File.Create(fileName);
            await JsonSerializer.SerializeAsync(stream, tree);
        }

        private string GetCacheFileName()
        {
            return Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "tree.json");
        }

        private Tree MergeTrees(Tree result1, Tree result2)
        {
            return new Tree(result1.Roots.Concat(result2.Roots));
        }

        public event EventHandler Changed;
    }
}
