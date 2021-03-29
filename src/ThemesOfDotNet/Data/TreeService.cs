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
using Microsoft.Extensions.Logging;
using Octokit;

namespace ThemesOfDotNet.Data
{
    public sealed class TreeService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly TreeProvider[] _treeProviders;
        private readonly ILogger _logger;
        private LoadTreeJob _loadTreeJob;

        public TreeService(IWebHostEnvironment environment, GitHubTreeProvider gitHubTreeProvider, AzureDevOpsTreeProvider azureTreeProvider, ILogger<TreeService> logger)
        {
            _environment = environment;
            _treeProviders = new TreeProvider[] { gitHubTreeProvider, azureTreeProvider };
            _logger = logger;
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

            public LoadTreeJob(Tree oldTree, DateTimeOffset? lastLoadDateTime, Func<CancellationToken, Task<Tree>> treeLoader)
            {
                _oldTree = oldTree;
                LoadDateTime = lastLoadDateTime;
                _cancellationTokenSource = new CancellationTokenSource();
                _stopwatch = Stopwatch.StartNew();
                _treeTask = treeLoader(_cancellationTokenSource.Token);
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
                    LoadDateTime = DateTimeOffset.Now;
                    return true;
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
                finally
                {
                    _stopwatch.Stop();
                }
            }

            public DateTimeOffset? LoadDateTime { get; private set; }

            public TimeSpan? LoadDuration => _treeTask.IsCompleted ? _stopwatch.Elapsed : (TimeSpan?) null;

            public Tree Tree => _tree ?? _oldTree;
        }

        public async Task InvalidateAsync(bool force = false)
        {
            var oldJob = _loadTreeJob;
            var newJob = new LoadTreeJob(Tree, oldJob?.LoadDateTime, ct => LoadTree(force, ct));

            // If we have a job pending, cancel it but wait for it to finish
            // so we don't accidentally trigger an abuse detection because of
            // too many pending async calls against the GitHub API.

            if (oldJob != null)
            {
                oldJob.Cancel();
                try
                {
                    await oldJob.WaitForLoad();
                }
                catch
                {
                    // Ignore
                }
            }

            Interlocked.CompareExchange(ref _loadTreeJob, newJob, oldJob);

            try
            {
                if (await newJob.WaitForLoad())
                    Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }
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
            var treeTasks = new List<Task<Tree>>();

            foreach (var provider in _treeProviders)
            {
                var func = new Func<Task<Tree>>(async () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var succeeded = false;
                    try
                    {
                        var result = await provider.GetTreeAsync(cancellationToken);
                        succeeded = true;
                        return result;
                    }
                    finally
                    {
                        var status = succeeded ? "completed" : "failed";
                        _logger.LogDebug($"Tree provider {provider.Name} {status} in {stopwatch.Elapsed}.");
                    }
                });

                var task = func();                    
                treeTasks.Add(task);
            }

            try
            {
                await Task.WhenAll(treeTasks);
            }
            catch
            {
                // Ignore any errors, those are handled below
            }

            var trees = new List<Tree>();

            for (var i = 0; i < treeTasks.Count; i++)
            {
                var name = _treeProviders[i].Name;
                var task = treeTasks[i];

                if (task.IsFaulted)
                {
                    _logger.LogError(task.Exception, $"Error loading data from {name}: {task.Exception.Message}");
                }
                else
                {
                    trees.Add(task.Result);
                }
            }

            if (trees.Count == 0)
                return Tree.Empty;

            while (trees.Count > 1)
            {
                var first = trees[0];
                var second = trees[1];
                var merged = MergeTrees(first, second);
                trees.RemoveAt(1);
                trees[0] = merged;
            }

            var result = trees[0];
            return SortTree(result);
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

        private static Tree MergeTrees(Tree result1, Tree result2)
        {
            return new Tree(result1.Roots.Concat(result2.Roots));
        }

        private static Tree SortTree(Tree tree)
        {
            var roots = tree.Roots.ToList();
            SortNodes(roots);
            return new Tree(roots);
        }

        private static void SortNodes(List<TreeNode> nodes)
        {
            nodes.Sort();

            foreach (var node in nodes)
                SortNodes(node.Children);
        }

        public event EventHandler Changed;
    }
}
