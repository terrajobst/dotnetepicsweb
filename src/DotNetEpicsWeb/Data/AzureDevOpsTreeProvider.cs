using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DotNetEpicsWeb.Data
{
    public sealed class AzureDevOpsTreeProvider
    {
        private readonly IConfiguration _configuration;

        public AzureDevOpsTreeProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private async Task<IReadOnlyList<AzureWorkItem>> GetWorkItemRootsAsync()
        {
            var url = new Uri(_configuration["AzureDevOpsUrl"]);
            var token = _configuration["AzureDevOpsToken"];
            var connection = new VssConnection(url, new VssBasicCredential(string.Empty, token));
            var client = connection.GetClient<WorkItemTrackingHttpClient>();
            var itemQuery = new Wiql
            {
                Query = @"
                    SELECT  [System.Id]
                    FROM    workitems
                "
            };

            var itemQueryResults = await client.QueryByWiqlAsync(itemQuery);
            var itemIds = itemQueryResults.WorkItems.Select(item => item.Id);
            var items = await client.GetWorkItemsAsync(itemIds, expand: WorkItemExpand.All);

            var linkQuery = new Wiql
            {
                Query = @"
                    SELECT  [System.Id]
                    FROM    WorkItemLinks
                    WHERE   ([System.Links.LinkType] = 'Parent')
                "
            };

            var workItemById = new Dictionary<int, AzureWorkItem>();

            foreach (var item in items)
            {
                var workItem = new AzureWorkItem();
                workItem.Id = item.Id.Value;
                workItem.Type = item.Fields["System.WorkItemType"].ToString();
                workItem.Title = item.Fields["System.Title"].ToString();
                workItem.State = item.Fields["System.State"].ToString();
                workItem.Priority = (long)item.Fields["Microsoft.VSTS.Common.Priority"];
                workItem.CreatedAt = (DateTime)item.Fields["System.CreatedDate"];
                workItem.CreatedBy = ((IdentityRef)item.Fields["System.CreatedBy"]).UniqueName;
                workItem.Url = item.Links.Links.Where(l => l.Key == "html")
                                               .Select(l => l.Value)
                                               .OfType<ReferenceLink>()
                                               .Select(l => l.Href)
                                               .SingleOrDefault();

                workItemById.Add(workItem.Id, workItem);

            }

            var linkQueryResults = await client.QueryByWiqlAsync(linkQuery);

            foreach (var link in linkQueryResults.WorkItemRelations)
            {
                if (link.Source == null || link.Target == null)
                    continue;

                var childId = link.Source.Id;
                var parentId = link.Target.Id;

                if (workItemById.TryGetValue(childId, out var child) &&
                    workItemById.TryGetValue(parentId, out var parent))
                {
                    parent.Children.Add(child);
                }
            }

            return workItemById.Values.OrderBy(v => v.Id).ToArray();
        }

        public async Task<Tree> GetTreeAsync()
        {
            var workItemRoots = await GetWorkItemRootsAsync();
            var themeNode = new TreeNode
            {
                Id = "azdo",
                IsPrivate = true,
                Title = _configuration["AzureDevOpsQueryTitle"],
                Url = _configuration["AzureDevOpsQueryUrl"],
                Labels = new[]
                {
                    new TreeNodeLabel
                    {
                        Name = "Theme",
                        BackgroundColor = "800080"
                    }
                }
            };

            ConvertNodes(themeNode.Children, workItemRoots);

            themeNode.CreatedAt = themeNode.Descendants().DefaultIfEmpty().Select(n => n.CreatedAt).Min();

            return new Tree(new[] { themeNode });
        }

        private static void ConvertNodes(List<TreeNode> treeNodes, IEnumerable<AzureWorkItem> azureNodes)
        {
            foreach (var azureNode in azureNodes)
            {
                var treeNode = ConvertNode(azureNode);
                treeNodes.Add(treeNode);

                ConvertNodes(treeNode.Children, azureNode.Children);
            }
        }

        private static TreeNode ConvertNode(AzureWorkItem azureNode)
        {
            var treeNode = new TreeNode
            {
                Id = $"azdo#{azureNode.Id}",
                IsPrivate = true,
                CreatedAt = azureNode.CreatedAt,
                CreatedBy = azureNode.CreatedBy,
                // IsClosed = 
                Title = azureNode.Title,
                // Milestone =
                Assignees = Array.Empty<string>(),
                Labels = CreateLabels(azureNode),
                Kind = ConvertKind(azureNode.Type),
                ReleaseInfo = new TreeNodeStatus { Status = azureNode.State },
                Url = azureNode.Url
            };
            return treeNode;
        }

        private static IReadOnlyList<TreeNodeLabel> CreateLabels(AzureWorkItem azureNode)
        {
            var result = new List<TreeNodeLabel>();
            var kind = ConvertKind(azureNode.Type);

            if (kind == TreeNodeKind.Epic)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = "Epic",
                    BackgroundColor = "c6415a",
                });
            }
            else if (kind == TreeNodeKind.UserStory)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = "User Story",
                    BackgroundColor = "0e8a16",
                });
            }

            if (azureNode.Priority == 0)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority: 0",
                    BackgroundColor = "b60205",
                });
            }
            else if (azureNode.Priority == 1)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority: 1",
                    BackgroundColor = "d93f0b",
                });
            }
            else if (azureNode.Priority == 2)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority: 2",
                    BackgroundColor = "e99695",
                });
            }
            else if (azureNode.Priority == 3)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority: 3",
                    BackgroundColor = "f9d0c4",
                });
            }

            return result.ToArray();
        }

        private static TreeNodeKind ConvertKind(string type)
        {
            if (string.Equals(type, "Epic", StringComparison.OrdinalIgnoreCase))
                return TreeNodeKind.Epic;
            else if (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase))
                return TreeNodeKind.UserStory;
            else
                return TreeNodeKind.Issue;
        }

        private sealed class AzureWorkItem
        {
            public int Id { get; set; }
            public string Type { get; set; }
            public string Title { get; set; }
            public string State { get; set; }
            public long Priority { get; set; }
            public DateTime CreatedAt { get; set; }
            public string CreatedBy { get; set; }
            public string Url { get; set; }
            public List<AzureWorkItem> Children { get; } = new List<AzureWorkItem>();
        }
    }
}
