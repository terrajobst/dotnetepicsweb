using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Humanizer;

using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Configuration;

using Octokit;

namespace ThemesOfDotNet.Data
{
    public sealed class GitHubTreeProvider
    {
        private readonly GitHubRepoId[] _repos;
        private readonly GitHubClientFactory _gitHubClientFactory;

        public GitHubTreeProvider(IConfiguration configuration,
                                  GitHubClientFactory gitHubClientFactory)
        {
            _repos = configuration["Repos"].Split(",").Select(GitHubRepoId.Parse).ToArray();
            _gitHubClientFactory = gitHubClientFactory;
        }

        public async Task<Tree> GetTreeAsync()
        {
            var client = await _gitHubClientFactory.CreateAsync();
            var repoCache = new RepoCache(client);

            var cardsTask = GetIssueCardsAsync(client);

            // Get root issues

            var issueTasks = new List<Task<IReadOnlyList<GitHubIssue>>>();

            foreach (var repoId in _repos)
            {
                foreach (var label in ThemesOfDotNetConstants.Labels)
                {
                    var task = GetIssuesAsync(client, repoCache, repoId, label);
                    issueTasks.Add(task);
                }
            }

            await Task.WhenAll(issueTasks);

            // Now parse all issue bodies to find children

            var rootIssues = issueTasks.SelectMany(t => t.Result).ToArray();
            var issueQueue = new Queue<GitHubIssue>(rootIssues);
            var issueById = rootIssues.ToDictionary(i => i.Id);
            var issueChildren = new Dictionary<string, List<GitHubIssue>>();

            while (issueQueue.Count > 0)
            {
                var issue = issueQueue.Dequeue();

                var links = ParseIssueLinks(issue.Id.Owner, issue.Id.Repo, issue.DescriptionMarkdown).ToArray();
                issueChildren.Add(issue.Id.ToString(), new List<GitHubIssue>());

                foreach (var (type, linkedId) in links)
                {
                    if (!issueById.TryGetValue(linkedId, out var linkedIssue))
                    {
                        linkedIssue = await GetIssueAsync(client, repoCache, linkedId);
                        issueById.Add(linkedId, linkedIssue);
                        issueQueue.Enqueue(linkedIssue);
                    }

                    var parent = type == IssueLinkType.Parent ? linkedIssue : issue;
                    var child = type == IssueLinkType.Child ? linkedIssue : issue;

                    if (!issueChildren.TryGetValue(parent.Id.ToString(), out var children))
                    {
                        children = new List<GitHubIssue>();
                        issueChildren.Add(parent.Id.ToString(), children);
                    }

                    children.Add(child);
                }
            }

            // Associate project status with issues

            var cards = await cardsTask;

            foreach (var card in cards)
            {
                if (issueById.TryGetValue(card.Id, out var issue))
                    issue.ProjectStatus = card.Status;
            }

            // Fix issue titles

            var regex = string.Join("|", ThemesOfDotNetConstants.Labels.Select(l => $" *^\\[?{l}\\]? *:? *"));

            foreach (var issue in issueById.Values)
            {
                var match = Regex.Match(issue.Title, regex);
                if (match.Success)
                    issue.Title = issue.Title.Substring(match.Length).Trim();
            }

            // Now build the tree out of that

            var nodeByIssue = issueById.Values.ToDictionary(i => i, i => ConvertToNode(i));

            foreach (var node in nodeByIssue.Values.OrderBy(n => n.Id))
            {
                foreach (var linkedIssue in issueChildren[node.Id])
                {
                    var linkedNode = nodeByIssue[linkedIssue];
                    node.Children.Add(linkedNode);

                    if (linkedNode.Parent == null)
                        linkedNode.Parent = node;
                }
            }

            foreach (var node in nodeByIssue.Values)
            {
                // In case where somone created a cycle, let's remove
                // the nodes that are already part of someone else.
                node.Children.RemoveAll(n => n.Parent != node);
            }

            var roots = nodeByIssue.Values.Where(n => n.Parent == null);
            return new Tree(roots);
        }

        private TreeNode ConvertToNode(GitHubIssue issue)
        {
            var treeNode = new TreeNode
            {
                Id = issue.Id.ToString(),
                IsPrivate = issue.IsPrivate,
                IsBottomUp = ConvertIsBottomUp(issue),
                CreatedAt = issue.CreatedAt,
                CreatedBy = issue.CreatedBy,
                IsClosed = issue.IsClosed,
                Title = issue.Title,
                Priority = ConvertPriority(issue),
                Cost = ConvertCost(issue),
                Milestone = issue.Milestone,
                Assignees = issue.Assignees,
                Labels = issue.Labels,
                Kind = issue.Kind,
                ReleaseInfo = issue.ProjectStatus,
                Url = issue.Url
            };

            return treeNode;
        }

        private bool ConvertIsBottomUp(GitHubIssue issue)
        {
            return issue.Labels.Any(l => string.Equals(l.Name, ThemesOfDotNetConstants.LabelBottomUpWork) ||
                                         string.Equals(l.Name, ThemesOfDotNetConstants.LabelContinuousImprovement));
        }

        private int? ConvertPriority(GitHubIssue issue)
        {
            var result = (int?)null;

            foreach (var label in issue.Labels)
            {
                if (!TryParseColonSeparatedValue(label.Name, out var name, out var value))
                    continue;

                if (!int.TryParse(value, out var priority))
                    continue;

                if (result == null || result > priority)
                    result = priority;
            }

            return result;
        }

        private TreeNodeCost? ConvertCost(GitHubIssue issue)
        {
            var result = (TreeNodeCost?)null;

            foreach (var label in issue.Labels)
            {
                if (!TryParseColonSeparatedValue(label.Name, out var name, out var value))
                    continue;

                TreeNodeCost? cost = null;

                if (string.Equals(value, "S", StringComparison.OrdinalIgnoreCase))
                    cost = TreeNodeCost.Small;
                else if (string.Equals(value, "M", StringComparison.OrdinalIgnoreCase))
                    cost = TreeNodeCost.Medium;
                else if (string.Equals(value, "L", StringComparison.OrdinalIgnoreCase))
                    cost = TreeNodeCost.Large;
                else if (string.Equals(value, "XL", StringComparison.OrdinalIgnoreCase))
                    cost = TreeNodeCost.ExtraLarge;

                if (cost != null)
                {
                    if (result == null || result < cost)
                        result = cost;
                }
            }

            return result;
        }

        private static bool TryParseColonSeparatedValue(string text, out string name, out string value)
        {
            name = null;
            value = null;

            var parts = text.Split(':');
            if (parts.Length != 2)
                return false;

            name = parts[0].Trim();
            value = parts[1].Trim();
            return true;
        }

        private async Task<IReadOnlyList<GitHubIssueCard>> GetIssueCardsAsync(GitHubClient client)
        {
            var issueCards = new List<GitHubIssueCard>();

            static bool IsDotNetReleaseProject(string name)
            {
                if (!name.StartsWith(".NET", StringComparison.OrdinalIgnoreCase))
                    return false;

                var version = name[4..].Trim();
                return Version.TryParse(version, out _);
            }

            var orgs = _repos.Select(r => r.Owner).Distinct();

            foreach (var org in orgs)
            {
                var projectRequest = new ProjectRequest(ItemStateFilter.Open);
                var projects = await client.Repository.Project.GetAllForOrganization(org, projectRequest);

                foreach (var project in projects)
                {
                    if (!IsDotNetReleaseProject(project.Name))
                        continue;

                    var columns = await client.Repository.Project.Column.GetAll(project.Id);

                    foreach (var column in columns)
                    {
                        var cards = await client.Repository.Project.Card.GetAll(column.Id);

                        foreach (var card in cards)
                        {
                            if (GitHubIssueId.TryParse(card.ContentUrl, out var issueId) ||
                                GitHubIssueId.TryParse(card.Note, out issueId))
                            {
                                var issueCard = new GitHubIssueCard(issueId, project.Name, column.Name);
                                issueCards.Add(issueCard);
                            }
                        }
                    }
                }
            }

            return issueCards.ToArray();
        }

        private static async Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(GitHubClient client, RepoCache repoCache, GitHubRepoId repoId, string label)
        {
            var repository = await repoCache.GetRepoAsync(repoId);

            var issueRequest = new RepositoryIssueRequest();
            issueRequest.State = ItemStateFilter.All;
            issueRequest.Labels.Add(label);
            var issues = await client.Issue.GetAllForRepository(repoId.Owner, repoId.Name, issueRequest);

            var result = new List<GitHubIssue>();

            foreach (var issue in issues)
            {
                var gitHubIssue = CreateGitHubIssue(repoId.Owner, repoId.Name, repository.Private, issue);
                result.Add(gitHubIssue);
            }

            return result;
        }

        private static async Task<GitHubIssue> GetIssueAsync(GitHubClient client, RepoCache repoCache, GitHubIssueId id)
        {
            var repo = await repoCache.GetRepoAsync(new GitHubRepoId(id.Owner, id.Repo));
            var issue = await client.Issue.Get(id.Owner, id.Repo, id.Number);
            return CreateGitHubIssue(id.Owner, id.Repo, repo.Private, issue);
        }

        private static GitHubIssue CreateGitHubIssue(string owner, string repo, bool isPrivate, Issue issue)
        {
            var result = new GitHubIssue
            {
                Id = new GitHubIssueId(owner, repo, issue.Number),
                IsPrivate = isPrivate,
                CreatedAt = issue.CreatedAt,
                CreatedBy = issue.User.Login,
                IsClosed = issue.ClosedAt != null,
                Title = issue.Title,
                DescriptionMarkdown = issue.Body,
                Assignees = issue.Assignees.Select(a => a.Login).ToArray(),
                Milestone = issue.Milestone?.Title,
                Labels = issue.Labels.Select(l => CreateLabel(l)).ToArray()
            };

            bool SetKindWhenContainsLabel(TreeNodeKind kind, string labelName, bool force = false)
            {
                if (force || result.Labels.Any(l => string.Equals(l.Name, labelName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Kind = kind;
                    return true;
                }

                return false;
            }

            if (!SetKindWhenContainsLabel(TreeNodeKind.Theme, ThemesOfDotNetConstants.LabelTheme))
            {
                if (!SetKindWhenContainsLabel(TreeNodeKind.Epic, ThemesOfDotNetConstants.LabelEpic))
                {
                    if (!SetKindWhenContainsLabel(TreeNodeKind.UserStory, ThemesOfDotNetConstants.LabelUserStory))
                    {
                        SetKindWhenContainsLabel(TreeNodeKind.Issue, ThemesOfDotNetConstants.LabelIssue, force: true);
                    }
                }
            }

            return result;
        }

        private static TreeNodeLabel CreateLabel(Label label)
        {
            var result = new TreeNodeLabel();
            result.Name = label.Name;
            result.BackgroundColor = label.Color;
            return result;
        }

        private static IEnumerable<(IssueLinkType, GitHubIssueId)> ParseIssueLinks(string owner, string repo, string markdown)
        {
            var pipeline = new MarkdownPipelineBuilder()
                .UseTaskLists()
                .UseAutoLinks()
                .Build();

            var document = MarkdownParser.Parse(markdown, pipeline);

            var parentLinks = document.Descendants<LinkInline>()
                                      .Where(l => !l.ContainsParentOfType<TaskList>())
                                      .Where(l => (l.FirstChild?.ToString()?.Trim() ?? string.Empty).StartsWith("Parent"))
                                      .ToArray();

            foreach (var parentLink in parentLinks)
            {
                if (GitHubIssueId.TryParse(parentLink.Url, out var id))
                {
                    yield return (IssueLinkType.Parent, id);
                    break;
                }
            }

            var taskLinkItems = document.Descendants<TaskList>().Select(t => t.Parent);

            foreach (var taskListItem in taskLinkItems)
            {
                var links = taskListItem.Descendants<LinkInline>();

                GitHubIssueId? id = null;

                foreach (var link in links)
                {
                    if (GitHubIssueId.TryParse(link.Url, out var i))
                    {
                        id = i;
                        break;
                    }
                }

                if (id == null)
                {
                    var autoLinks = taskListItem.Descendants<AutolinkInline>();

                    foreach (var autoLink in autoLinks)
                    {
                        if (GitHubIssueId.TryParse(autoLink.Url, out var i))
                        {
                            id = i;
                            break;
                        }
                    }
                }

                if (id == null)
                {
                    var literalInlines = taskListItem.Descendants<LiteralInline>();

                    foreach (var literalInline in literalInlines)
                    {
                        if (id != null)
                            break;

                        foreach (Match match in Regex.Matches(literalInline.Content.ToString(), "#(?<number>[0-9]+)"))
                        {
                            var numberText = match.Groups["number"].Value;
                            if (int.TryParse(numberText, out var number))
                            {
                                id = new GitHubIssueId(owner, repo, number);
                                break;
                            }
                        }
                    }
                }

                if (id != null)
                    yield return (IssueLinkType.Child, id.Value);
            }
        }

        private sealed class RepoCache
        {
            private readonly GitHubClient _client;
            private readonly ConcurrentDictionary<GitHubRepoId, Repository> _repos = new ConcurrentDictionary<GitHubRepoId, Repository>();

            public RepoCache(GitHubClient client)
            {
                _client = client;
            }

            public async Task<Repository> GetRepoAsync(GitHubRepoId id)
            {
                if (!_repos.TryGetValue(id, out var result))
                {
                    result = await _client.Repository.Get(id.Owner, id.Name);
                    if (!_repos.TryAdd(id, result))
                        result = _repos[id];
                }

                return result;
            }
        }

        private enum IssueLinkType
        {
            Parent,
            Child
        }

        private sealed class GitHubIssue
        {
            public GitHubIssueId Id { get; set; }
            public bool IsPrivate { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public string CreatedBy { get; set; }
            public bool IsClosed { get; set; }
            public string Title { get; set; }
            public string DescriptionMarkdown { get; set; }
            public string Milestone { get; set; }
            public IReadOnlyList<string> Assignees { get; set; }
            public IReadOnlyList<TreeNodeLabel> Labels { get; set; }

            public TreeNodeKind Kind { get; set; }
            public TreeNodeStatus ProjectStatus { get; set; }
            public string Url => $"https://github.com/{Id.Owner}/{Id.Repo}/issues/{Id.Number}";
        }

        private sealed class GitHubIssueCard
        {
            public GitHubIssueCard(GitHubIssueId id, string projectName, string column)
            {
                Id = id;
                Status = new TreeNodeStatus
                {
                    Release = projectName,
                    Status = column
                };
            }

            public GitHubIssueId Id { get; }
            public TreeNodeStatus Status { get; }

            public override string ToString()
            {
                return $"{Id} - {Status}";
            }
        }
    }
}
