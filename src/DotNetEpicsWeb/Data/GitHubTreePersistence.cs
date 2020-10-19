using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubTreePersistence
    {
        private readonly string _fileName;
        private readonly JsonSerializerOptions _options = CreateOptions();

        public GitHubTreePersistence(IWebHostEnvironment environment)
        {
            _fileName = Path.Combine(environment.ContentRootPath, "tree.json");
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = {
                    new GitHubIssueIdJsonConverter()
                }
            };
            return options;
        }

        public async Task SaveAsync(GitHubIssueTree tree)
        {
            using (var stream = File.Create(_fileName))
                await JsonSerializer.SerializeAsync(stream, tree, _options);
        }

        public async Task<GitHubIssueTree> LoadAsync()
        {
            using (var stream = File.OpenRead(_fileName))
            {
                var tree = await JsonSerializer.DeserializeAsync<GitHubIssueTree>(stream, _options);
                SetParents(null, tree.Roots);
                return tree;
            }
        }

        private static void SetParents(GitHubIssueNode parent, List<GitHubIssueNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.Parent = parent;
                SetParents(node, node.Children);
            }
        }
    }
}
