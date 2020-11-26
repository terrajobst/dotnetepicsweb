using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        private LoadTreeJob _loadTreeJob;

        public TreeService(IWebHostEnvironment environment, GitHubTreeProvider gitHubTreeProvider, AzureDevOpsTreeProvider azureTreeProvider)
        {
            _environment = environment;
            _githubTreeProvider = gitHubTreeProvider;
            _azureTreeProvider = azureTreeProvider;
        }

        public Tree Tree => _loadTreeJob?.Tree;

        public DateTimeOffset? LoadDateTime => _loadTreeJob?.LoadDateTime;

        public TimeSpan? LoadDuration => _loadTreeJob?.LoadDuration;

        private sealed class LoadTreeJob
        {
            private readonly Tree _oldTree;
            private readonly CancellationTokenSource _cancellationTokenSource;
            private readonly Stopwatch _stopwatch;
            private readonly Task<Tree> _treeTask;
            private Tree _tree;

            public LoadTreeJob(Tree oldTree, Func<CancellationToken, Task<Tree>> treeLoader)
            {
                _oldTree = oldTree;
                _cancellationTokenSource = new CancellationTokenSource();
                _stopwatch = Stopwatch.StartNew();
                _treeTask = treeLoader(_cancellationTokenSource.Token);
                LoadDateTime = DateTimeOffset.Now;
            }

            public void Cancel()
            {
                _cancellationTokenSource.Cancel();
            }

            public async Task<bool> WaitForLoad()
            {
                try
                {
                    _tree = await _treeTask;
                    _stopwatch.Stop();
                    return true;
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }

            public DateTimeOffset LoadDateTime { get; }

            public TimeSpan? LoadDuration => _treeTask.IsCompleted ? _stopwatch.Elapsed : (TimeSpan?) null;

            public Tree Tree => _tree ?? _oldTree;
        }

        public async Task InvalidateAsync(bool force = false)
        {
            var oldJob = _loadTreeJob;
            var newJob = new LoadTreeJob(Tree, ct => LoadTree(force, ct));

            if (oldJob != null)
                oldJob.Cancel();

            Interlocked.CompareExchange(ref _loadTreeJob, newJob, oldJob);

            if (await newJob.WaitForLoad())
                Changed?.Invoke(this, EventArgs.Empty);
        }

        private async Task<Tree> LoadTree(bool force, CancellationToken cancellationToken)
        {
            if (!_environment.IsDevelopment())
            {
                return await LoadTreeFromProvidersAsync(cancellationToken);
            }
            else
            {
                var tree = force ? null : await LoadTreeFromCacheAsync();
                if (tree == null)
                    tree = await LoadTreeFromProvidersAsync(cancellationToken);
                await SaveTreeToCacheAsync(tree);
                return tree;
            }
        }

        private async Task<Tree> LoadTreeFromProvidersAsync(CancellationToken cancellationToken)
        {
            var gitHubTreeTask = _githubTreeProvider.GetTreeAsync(cancellationToken);
            var azureTreeTask = _azureTreeProvider.GetTreeAsync(cancellationToken);
            await Task.WhenAll(gitHubTreeTask, azureTreeTask);
            return SortTree(MergeTrees(gitHubTreeTask.Result, azureTreeTask.Result));
        }

        private async Task<Tree> LoadTreeFromCacheAsync()
        {
            var fileName = GetCacheFileName();
            if (!File.Exists(fileName))
                return null;

            using var stream = File.OpenRead(fileName);
            var result = await JsonSerializer.DeserializeAsync<Tree>(stream);
            result.Initialize();
            return result;
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

        private Tree SortTree(Tree tree)
        {
            var roots = tree.Roots.ToList();
            SortNodes(roots);
            return new Tree(roots);
        }

        private void SortNodes(List<TreeNode> nodes)
        {
            nodes.Sort();

            foreach (var node in nodes)
                SortNodes(node.Children);
        }

        public event EventHandler Changed;
    }
}
