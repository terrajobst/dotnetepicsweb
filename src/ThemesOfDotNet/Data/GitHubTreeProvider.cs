using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Octokit;

namespace ThemesOfDotNet.Data
{
    public sealed class GitHubTreeProvider : TreeProvider
    {
        private readonly ILogger<GitHubTreeProvider> _logger;
        private readonly GitHubRepoId[] _repos;
        private readonly GitHubClientFactory _gitHubClientFactory;

        public GitHubTreeProvider(ILogger<GitHubTreeProvider> logger,
                                  IConfiguration configuration,
                                  GitHubClientFactory gitHubClientFactory)
        {
            _repos = configuration["Repos"].Split(",").Select(GitHubRepoId.Parse).ToArray();
            _logger = logger;
            _gitHubClientFactory = gitHubClientFactory;
        }

        public override async Task<Tree> GetTreeAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"Loading GitHub tree for repos {string.Join(",", _repos)}");

            var client = await _gitHubClientFactory.CreateAsync();
            var repoCache = new RepoCache(client);

            var cardsTask = GetIssueCardsAsync(client, cancellationToken);

            // Get root issues

            _logger.LogDebug($"Loading GitHub root issues...");

            var startingIssueTasks = new List<Task<IReadOnlyList<GitHubIssue>>>();

            foreach (var repoId in _repos)
            {
                foreach (var label in ThemesOfDotNetConstants.Labels)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var task = GetIssuesAsync(client, repoCache, repoId, label);
                    startingIssueTasks.Add(task);
                }
            }

            await Task.WhenAll(startingIssueTasks);

            // Now parse all issue bodies to find children

            _logger.LogDebug($"Loading GitHub issue details...");

            var startingIssues = startingIssueTasks.SelectMany(t => t.Result).ToArray();
            var issueQueue = new Queue<GitHubIssue[]>();
            issueQueue.Enqueue(startingIssues);
            var issueById = startingIssues.ToDictionary(i => i.Id);
            var issues = new List<GitHubIssue>(startingIssues);
            var issueChildren = new Dictionary<string, List<GitHubIssue>>(StringComparer.OrdinalIgnoreCase);

            while (issueQueue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var issueBatch = issueQueue.Dequeue();
                var nextBatch = new List<GitHubIssue>();

                var issueLinks = issueBatch.Select(i => (Issue: i, Links: ParseIssueLinks(i.Id.Owner, i.Id.Repo, i.DescriptionMarkdown).ToArray()))
                                           .ToArray();

                var unknownIssueIds = issueLinks.SelectMany(l => l.Links)
                                                .Select(l => l.LinkedId)
                                                .Distinct()
                                                .Where(id => !issueById.ContainsKey(id));

                var newIssues = await GetIssuesBatchedAsync(client, repoCache, unknownIssueIds);

                foreach (var (referencedId, issue) in newIssues)
                {
                    var linkedIssue = issue;
                    if (linkedIssue == null)
                        continue;

                    var addIssue = true;

                    if (linkedIssue.Id != referencedId)
                    {
                        _logger.LogDebug($"GitHub issue was transferred from {referencedId} to {linkedIssue.Id}.");

                        // That means the issue got transferred. Let's try again with the new id.

                        if (!issueById.TryGetValue(linkedIssue.Id, out var existingIssue))
                        {
                            // We haven't fetched the issue yet but, we still want to record it
                            // under the new ID as well.
                            issueById.Add(linkedIssue.Id, linkedIssue);
                        }
                        else
                        {
                            // OK, we already fetched the issue. Now let's just associate the old id
                            // with the existing issue and not add the issue we just retrieved.
                            issueById.Add(referencedId, existingIssue);
                            linkedIssue = existingIssue;
                            addIssue = false;
                        }
                    }

                    if (addIssue && issueById.TryAdd(referencedId, linkedIssue))
                    {
                        issues.Add(linkedIssue);
                        nextBatch.Add(linkedIssue);
                    }
                }

                foreach (var (issue, links) in issueLinks)
                {
                    issueChildren.Add(issue.Id.ToString(), new List<GitHubIssue>());

                    foreach (var (linkType, linkedId) in links)
                    {
                        if (issueById.TryGetValue(linkedId, out var linkedIssue))
                        {
                            var parent = linkType == IssueLinkType.Parent ? linkedIssue : issue;
                            var child = linkType == IssueLinkType.Child ? linkedIssue : issue;

                            if (!issueChildren.TryGetValue(parent.Id.ToString(), out var children))
                            {
                                children = new List<GitHubIssue>();
                                issueChildren.Add(parent.Id.ToString(), children);
                            }

                            children.Add(child);
                        }
                        else
                        {
                            _logger.LogDebug($"Can't find linked issue {linkedId}.");
                        }
                    }
                }

                if (nextBatch.Any())
                    issueQueue.Enqueue(nextBatch.ToArray());
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

            foreach (var issue in issues)
            {
                var match = Regex.Match(issue.Title, regex);
                if (match.Success)
                    issue.Title = issue.Title.Substring(match.Length).Trim();
            }

            // Let's sort the issues by close state, kind, and then by ID.
            // This ensures that when we remove cycles we prefer to remove
            // them from closed issues and from "out of order" hierarchies.
            // For example, when a user story links to a theme, we want to
            // sever the connection between the user story and the theme,
            // rather than between the theme & epic or between the epic &
            // user story.

            issues.Sort((x, y) =>
            {
                var result = x.IsClosed.CompareTo(y.IsClosed);
                if (result == 0)
                {
                    result = x.Kind.CompareTo(y.Kind);
                    if (result == 0)
                    {
                        result = x.Title.CompareTo(y.Title);
                        if (result == 0)
                            result = x.Id.CompareTo(y.Id);
                    }
                }

                return result;
            });

            // Detect & fix cycles

            var ancestors = new HashSet<GitHubIssue>();

            foreach (var issue in issues)
            {
                ancestors.Clear();
                ancestors.Add(issue);
                EnsureNoCycles(_logger, issue, issueChildren, ancestors);
            }

            static void EnsureNoCycles(ILogger logger, GitHubIssue issue, Dictionary<string, List<GitHubIssue>> issueChildren, HashSet<GitHubIssue> ancestors)
            {
                var myChildren = issueChildren[issue.Id.ToString()];
                for (var i = myChildren.Count - 1; i >= 0; i--)
                {
                    var myChild = myChildren[i];
                    if (!ancestors.Add(myChild))
                    {
                        logger.LogDebug($"Cycle detected: Issue {myChild.Id} can't be a child of {issue.Id} because it's already an ancestor.");
                        myChildren.RemoveAt(i);
                    }
                    else
                    {
                        EnsureNoCycles(logger, myChild, issueChildren, ancestors);
                        ancestors.Remove(myChild);
                    }
                }
            }

            // When open issues are contained in multiple parents, we want to prefer parents that are still open

            var parentsByIssue = issues.SelectMany(parent => issueChildren[parent.Id.ToString()].Select(child => (parent, child)))
                                       .ToLookup(t => t.child, t => t.parent);

            foreach (var openChild in issues.Where(i => !i.IsClosed))
            {
                var hasOpenParents = parentsByIssue[openChild].Any(p => !p.IsClosed);

                if (hasOpenParents)
                {
                    var closedParents = parentsByIssue[openChild].Where(p => p.IsClosed);
                    foreach (var closedParent in closedParents)
                    {
                        var children = issueChildren[closedParent.Id.ToString()];
                        _logger.LogDebug($"Removing {openChild.Id} from parent {closedParent.Id} because the parent is closed.");
                        children.Remove(openChild);
                    }
                }
            }

            // Now build the tree out of that

            var nodeByIssue = issues.ToDictionary(i => i, i => ConvertToNode(i));

            foreach (var node in nodeByIssue.Values.OrderBy(n => n.Id))
            {
                foreach (var linkedIssue in issueChildren[node.Id])
                {
                    var linkedNode = nodeByIssue[linkedIssue];
                    node.Children.Add(linkedNode);
                }
            }

            var roots = issues.Where(i => !parentsByIssue[i].Any())
                              .Select(i => nodeByIssue[i]);
            return new Tree(roots);
        }

        private async Task<IReadOnlyList<(GitHubIssueId IssueId, GitHubIssue Issue)>> GetIssuesBatchedAsync(GitHubClient client, RepoCache repoCache, IEnumerable<GitHubIssueId> ids)
        {
            const int BatchSize = 1;

            var result = new List<(GitHubIssueId IssueId, GitHubIssue Issue)>();
            var remainingIds = ids.ToList();

            while (remainingIds.Any())
            {
                var batchTasks = remainingIds.Take(BatchSize)
                                             .Select(id => (IssuedId: id, Task: GetIssueAsync(client, repoCache, id)))
                                             .ToArray();

                await Task.WhenAll(batchTasks.Select(t => t.Task));
                result.AddRange(batchTasks.Select(t => (t.IssuedId, t.Task.Result)));
                remainingIds.RemoveRange(0, Math.Min(remainingIds.Count, BatchSize));
            }

            return result.ToArray();
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
                Teams = ConvertTeams(issue),
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
            return issue.Labels.Any(l => string.Equals(l.Name, ThemesOfDotNetConstants.LabelBottomUpWork, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(l.Name, ThemesOfDotNetConstants.LabelContinuousImprovement, StringComparison.OrdinalIgnoreCase));
        }

        private int? ConvertPriority(GitHubIssue issue)
        {
            var result = (int?)null;

            foreach (var label in issue.Labels)
            {
                if (!TryParseNamedValue(label.Name, "Priority", out var value))
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
                if (!TryParseNamedValue(label.Name, "Cost", out var value))
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

        private IReadOnlyList<string> ConvertTeams(GitHubIssue issue)
        {
            var result = (List<string>)null;

            foreach (var label in issue.Labels)
            {
                if (!TryParseNamedValue(label.Name, "Team", out var value))
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

        private static bool TryParseNamedValue(string text, string name, out string value)
        {
            value = default;

            var parts = text.Split(':');
            if (parts.Length != 2)
                return false;

            var n = parts[0].Trim();
            var v = parts[1].Trim();

            if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return false;

            value = v;
            return true;
        }

        private async Task<IReadOnlyList<GitHubIssueCard>> GetIssueCardsAsync(GitHubClient client, CancellationToken cancellationToken)
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
                cancellationToken.ThrowIfCancellationRequested();

                var projectRequest = new ProjectRequest(ItemStateFilter.Open);
                var projects = await client.Repository.Project.GetAllForOrganization(org, projectRequest);

                foreach (var project in projects)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsDotNetReleaseProject(project.Name))
                        continue;

                    var columns = await client.Repository.Project.Column.GetAll(project.Id);

                    foreach (var column in columns)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

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

        private async Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(GitHubClient client, RepoCache repoCache, GitHubRepoId repoId, string label)
        {
            // NOTE: There is a bug in GitHub where if you ask for a non-existing label,
            //       it will return all issues. So let's first make sure the label exists.

            try
            {
                await client.Issue.Labels.Get(repoId.Owner, repoId.Name, label);
            }
            catch (NotFoundException)
            {
                _logger.LogDebug($"Repo {repoId} doesn't contain label {label}, returning an empty set");
                return Array.Empty<GitHubIssue>();
            }

            var repository = await repoCache.GetRepoAsync(repoId);

            var issueRequest = new RepositoryIssueRequest();
            issueRequest.State = ItemStateFilter.All;
            issueRequest.Labels.Add(label);
            var issues = await client.Issue.GetAllForRepository(repoId.Owner, repoId.Name, issueRequest);

            var result = new List<GitHubIssue>();

            foreach (var issue in issues)
            {
                if (issue.PullRequest != null)
                    continue;

                var gitHubIssue = CreateGitHubIssue(repository.Private, issue);

                // If an issue was transferred, we don't want to include it here.
                //
                // We can tell it was transferred if the returned issue number,
                // repo name or org name is different from the one we started
                // with.

                var wasTransferred = gitHubIssue.Id.Number != issue.Number ||
                                     !string.Equals(gitHubIssue.Id.Owner, repoId.Owner, StringComparison.OrdinalIgnoreCase) ||
                                     !string.Equals(gitHubIssue.Id.Repo, repoId.Name, StringComparison.OrdinalIgnoreCase);

                if (!wasTransferred)
                    result.Add(gitHubIssue);
                else
                    _logger.LogDebug($"Not including issue {repoId}#{issue.Number} in root set because it was transferred to {gitHubIssue.Id}.");
            }

            return result;
        }

        private async Task<GitHubIssue> GetIssueAsync(GitHubClient client, RepoCache repoCache, GitHubIssueId id)
        {
            var remainingRetryCount = 3;
        Retry:
            try
            {
                var issue = await client.Issue.Get(id.Owner, id.Repo, id.Number);
                if (issue.PullRequest != null)
                    return null;

                var effectiveIssueId = GitHubIssueId.Parse(issue.HtmlUrl);
                var repo = await repoCache.GetRepoAsync(new GitHubRepoId(effectiveIssueId.Owner, effectiveIssueId.Repo));
                return CreateGitHubIssue(repo.Private, issue);
            }
            catch (NotFoundException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading issue {id} ({remainingRetryCount} retries): {ex.Message}");

                remainingRetryCount--;
                if (remainingRetryCount >= 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    goto Retry;
                }

                return null;
            }
        }

        private static GitHubIssue CreateGitHubIssue(bool isPrivate, Issue issue)
        {
            var id = GitHubIssueId.Parse(issue.HtmlUrl);

            var result = new GitHubIssue
            {
                Id = id,
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
            result.Color = label.Color;
            return result;
        }

        private static IEnumerable<(IssueLinkType LinkType, GitHubIssueId LinkedId)> ParseIssueLinks(string owner, string repo, string markdown)
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

                        foreach (Match match in Regex.Matches(literalInline.Content.ToString(), "((?<owner>[a-zA-Z0-9-]+)/(?<repo>[a-zA-Z0-9-]+))?#(?<number>[0-9]+)"))
                        {
                            var linkOwner = match.Groups["owner"].Value;
                            var linkRepo = match.Groups["repo"].Value;
                            var numberText = match.Groups["number"].Value;

                            if (string.IsNullOrEmpty(linkOwner))
                            {
                                linkOwner = owner;
                                linkRepo = repo;
                            }

                            if (int.TryParse(numberText, out var number))
                            {
                                id = new GitHubIssueId(linkOwner, linkRepo, number);
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

            public override string ToString()
            {
                return $"{Id}: {Title}";
            }
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
