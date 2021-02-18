using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace ThemesOfDotNet.Data
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
            var azureDevOpsUrl = _configuration["AzureDevOpsUrl"];
            if (string.IsNullOrEmpty(azureDevOpsUrl))
            {
                return new List<AzureWorkItem>();
            }

            var url = new Uri(azureDevOpsUrl);
            var token = _configuration["AzureDevOpsToken"];
            var connection = new VssConnection(url, new VssBasicCredential(string.Empty, token));
            var client = connection.GetClient<WorkItemTrackingHttpClient>();
            var itemQueryResults = await client.QueryByIdAsync(new Guid(_configuration["AzureDevOpsQueryId"]));

            var itemIds = itemQueryResults.WorkItemRelations.Select(rel => rel.Target)
                  .Concat(itemQueryResults.WorkItemRelations.Select(rel => rel.Source))
                  .Where(r => r != null)
                  .Select(r => r.Id)
                  .ToHashSet();

            var items = await GetWorkItemsAsync(client, itemIds);

            var workItemById = new Dictionary<int, AzureWorkItem>();

            foreach (var item in items)
            {
                var workItem = new AzureWorkItem();
                workItem.Id = item.Id.Value;
                workItem.Type = item.Fields["System.WorkItemType"].ToString();
                workItem.Title = item.Fields["System.Title"].ToString();
                workItem.State = item.Fields["System.State"].ToString();

                if (item.Fields.TryGetValue<long>("Microsoft.VSTS.Common.Priority", out var priority))
                    workItem.Priority = priority;

                if (item.Fields.TryGetValue<string>("Microsoft.DevDiv.TshirtCosting", out var cost))
                    workItem.Cost = cost;

                if (item.Fields.TryGetValue<string>("Microsoft.eTools.Bug.Release", out var release))
                    workItem.Release = release;

                if (item.Fields.TryGetValue<string>("Microsoft.DevDiv.Target", out var target))
                    workItem.Target = target;

                if (item.Fields.TryGetValue<string>("Microsoft.DevDiv.Milestone", out var milestone))
                    workItem.Milestone = milestone;

                if (item.Fields.TryGetValue<IdentityRef>("System.AssignedTo", out var assignedTo))
                    workItem.AssignedTo = GetAlias(assignedTo);

                workItem.CreatedAt = (DateTime)item.Fields["System.CreatedDate"];
                workItem.CreatedBy = GetAlias((IdentityRef)item.Fields["System.CreatedBy"]);
                workItem.Url = item.Links.Links.Where(l => l.Key == "html")
                                               .Select(l => l.Value)
                                               .OfType<ReferenceLink>()
                                               .Select(l => l.Href)
                                               .SingleOrDefault();

                if (item.Fields.TryGetValue<string>("System.Tags", out var tagText))
                    workItem.Tags = tagText.Split(';');
                else
                    workItem.Tags = Array.Empty<string>();

                workItemById.Add(workItem.Id, workItem);
            }

            foreach (var link in itemQueryResults.WorkItemRelations)
            {
                if (link.Source == null || link.Target == null)
                    continue;

                if (link.Rel != "System.LinkTypes.Hierarchy-Forward")
                    continue;

                var parentId = link.Source.Id;
                var childId = link.Target.Id;

                if (workItemById.TryGetValue(childId, out var child) &&
                    workItemById.TryGetValue(parentId, out var parent))
                {
                    child.Parent = parent;
                    parent.Children.Add(child);
                }
            }

            return workItemById.Values.OrderBy(v => v.Id)
                                      .Where(n => n.Parent == null)
                                      .ToArray();
        }

        private async Task<List<WorkItem>> GetWorkItemsAsync(WorkItemTrackingHttpClient client, IEnumerable<int> ids)
        {
            var result = new List<WorkItem>();
            var batchedIds = Batch(ids, 200);
            foreach (var batch in batchedIds)
            {
                var items = await client.GetWorkItemsAsync(batch, expand: WorkItemExpand.All);
                result.AddRange(items);
            }

            return result;
        }

        private static IEnumerable<T[]> Batch<T>(IEnumerable<T> source, int batchSize)
        {
            var list = new List<T>(batchSize);

            foreach (var item in source)
            {
                if (list.Count == batchSize)
                {
                    yield return list.ToArray();
                    list.Clear();
                }
                list.Add(item);
            }

            yield return list.ToArray();
        }

        public async Task<Tree> GetTreeAsync(CancellationToken cancellationToken)
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

            themeNode.CreatedAt = themeNode.Descendants().DefaultIfEmpty().Select(n => n?.CreatedAt ?? DateTimeOffset.UtcNow).Min();

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
                IsBottomUp = IsBottomUp(azureNode),
                CreatedAt = azureNode.CreatedAt,
                CreatedBy = azureNode.CreatedBy,
                IsClosed = ConvertIsClosed(azureNode),
                Title = azureNode.Title,
                Milestone = ConvertMilestone(azureNode),
                Priority = ConvertPriority(azureNode.Priority),
                Cost = ConvertCost(azureNode.Cost),
                Teams = ConvertTeams(azureNode.Tags),
                Assignees = string.IsNullOrEmpty(azureNode.AssignedTo) 
                                ? Array.Empty<string>()
                                : new[] { azureNode.AssignedTo },
                Labels = CreateLabels(azureNode),
                Kind = ConvertKind(azureNode.Type),
                ReleaseInfo = new TreeNodeStatus { 
                    Release = ConvertRelease(azureNode),
                    Status = azureNode.State
                },
                Url = azureNode.Url
            };
            return treeNode;
        }

        private static bool IsBottomUp(AzureWorkItem azureNode)
        {
            return azureNode.Tags.Any(t => string.Equals(t, ThemesOfDotNetConstants.LabelBottomUpWork, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(t, ThemesOfDotNetConstants.LabelContinuousImprovement, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<TreeNodeLabel> CreateLabels(AzureWorkItem azureNode)
        {
            var result = new List<TreeNodeLabel>();
            var kind = ConvertKind(azureNode.Type);
            var cost = ConvertCost(azureNode.Cost);

            // Kind

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

            // Priorities

            if (azureNode.Priority == 0)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority:0",
                    BackgroundColor = "b60205",
                });
            }
            else if (azureNode.Priority == 1)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority:1",
                    BackgroundColor = "d93f0b",
                });
            }
            else if (azureNode.Priority == 2)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority:2",
                    BackgroundColor = "e99695",
                });
            }
            else if (azureNode.Priority == 3)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Priority:3",
                    BackgroundColor = "f9d0c4",
                });
            }

            // Cost

            if (cost == TreeNodeCost.Small)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Cost:S",
                    BackgroundColor = "bfdadc",
                });
            }
            else if (cost == TreeNodeCost.Medium)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Cost:M",
                    BackgroundColor = "c2e0c6",
                });
            }
            else if (cost == TreeNodeCost.Large)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Cost:L",
                    BackgroundColor = "0e8a16",
                });
            }
            else if (cost == TreeNodeCost.ExtraLarge)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = $"Cost:XL",
                    BackgroundColor = "006b75",
                });
            }

            // Tags

            foreach (var tag in azureNode.Tags)
            {
                result.Add(new TreeNodeLabel
                {
                    Name = tag,
                    BackgroundColor = "c5def5",
                });
            }

            return result.ToArray();
        }

        private static bool ConvertIsClosed(AzureWorkItem azureNode)
        {
            var closedStates = new[] { "Cut", "Completed" };
            return closedStates.Any(s => string.Equals(azureNode.State, s, StringComparison.OrdinalIgnoreCase));
        }

        private static TreeNodeKind ConvertKind(string type)
        {
            if (string.Equals(type, "Scenario", StringComparison.OrdinalIgnoreCase))
                return TreeNodeKind.Epic;
            else if (string.Equals(type, "Experience", StringComparison.OrdinalIgnoreCase))
                return TreeNodeKind.UserStory;
            else
                return TreeNodeKind.Issue;
        }

        private static int? ConvertPriority(long? priority)
        {
            if (priority >= 0 && priority <= 3)
                return (int)priority;

            return null;
        }

        private static TreeNodeCost? ConvertCost(string cost)
        {
            if (string.Equals(cost, "S", StringComparison.OrdinalIgnoreCase))
                return TreeNodeCost.Small;
            else if (string.Equals(cost, "M", StringComparison.OrdinalIgnoreCase))
                return TreeNodeCost.Medium;
            else if (string.Equals(cost, "L", StringComparison.OrdinalIgnoreCase))
                return TreeNodeCost.Large;
            else if (string.Equals(cost, "XL", StringComparison.OrdinalIgnoreCase))
                return TreeNodeCost.ExtraLarge;
            else
                return null;
        }

        private static IReadOnlyList<string> ConvertTeams(string[] tags)
        {
            var result = (List<string>)null;

            foreach (var tag in tags)
            {
                var parts = tag.Split(':');
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (!string.Equals(key, "Team", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (result == null)
                    result = new List<string>();

                result.Add(value);
            }

            if (result == null)
                return Array.Empty<string>();

            result.Sort();
            return result;
        }

        private static string ConvertRelease(AzureWorkItem azureNode)
        {
            // Release      : Dev16
            // Milestone    : 16.8
            //
            // --->
            //
            // VS 16.8

            var release = azureNode.Release;
            if (release != null)
            {
                if (release.StartsWith("Dev", StringComparison.OrdinalIgnoreCase))
                    release = "VS";
                else if (release.StartsWith("NET", StringComparison.OrdinalIgnoreCase) || release.StartsWith(".NET", StringComparison.OrdinalIgnoreCase))
                    release = ".NET SDK";
            }

            var milestone = azureNode.Milestone;

            var result = string.Join(" ", release, milestone).Trim();
            if (result.Length == 0)
                return null;

            return result;
        }

        private static string ConvertMilestone(AzureWorkItem azureNode)
        {
            // Milestone    : 16.8
            // Target       : Preview 2
            //
            // --->
            //
            // 16.8 P2

            var milestone = azureNode.Milestone;

            var target = azureNode.Target;
            if (target != null)
            {
                target = target.Replace(".NET", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("Preview ", "P", StringComparison.OrdinalIgnoreCase)
                               .Replace("Preview", "P", StringComparison.OrdinalIgnoreCase);
            }

            var result = string.Join(" ", milestone, target).Trim();
            if (result.Length == 0)
                return null;

            return result;
        }

        private static string GetAlias(IdentityRef identityRef)
        {
            var email = identityRef.UniqueName;
            var indexOfAt = email.IndexOf('@');
            return indexOfAt >= 0 
                ? email.Substring(0, indexOfAt)
                : email;
        }

        private sealed class AzureWorkItem
        {
            public int Id { get; set; }
            public string Type { get; set; }
            public string Title { get; set; }
            public string State { get; set; }
            public long? Priority { get; set; }
            public string Cost { get; set; }
            public string Milestone { get; set; }
            public string Target { get; set; }
            public string Release { get; set; }
            public DateTime CreatedAt { get; set; }
            public string CreatedBy { get; set; }
            public string AssignedTo { get; set; }
            public string Url { get; set; }
            public string[] Tags { get; set; }
            public AzureWorkItem Parent { get; set; }
            public List<AzureWorkItem> Children { get; } = new List<AzureWorkItem>();
        }
    }
}
