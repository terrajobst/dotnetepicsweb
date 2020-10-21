using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Configuration;

using Octokit;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubTreeService : IGitHubTreeService
    {
        private readonly GitHubRepoId[] _repos;
        private readonly GitHubClientFactory _gitHubClientFactory;
        private readonly GitHubTreePersistence _persistence;

        public GitHubTreeService(IConfiguration configuration,
                                 GitHubClientFactory gitHubClientFactory,
                                 GitHubTreePersistence persistence)
        {
            _repos = configuration["Repos"].Split(",").Select(GitHubRepoId.Parse).ToArray();
            _gitHubClientFactory = gitHubClientFactory;
            _persistence = persistence;
        }

        public async Task<GitHubIssueTree> GetIssueTreeAsync()
        {
            var client = await _gitHubClientFactory.CreateAsync();
            var tree = await GetIssueTreeAsync(client);

            await _persistence.SaveAsync(tree);

            return tree;
        }

        private async Task<GitHubIssueTree> GetIssueTreeAsync(GitHubClient client)
        {
            var cardsTask = GetIssueCards(client);

            // Get root issues

            var issueTasks = new List<Task<IReadOnlyList<GitHubIssue>>>();

            foreach (var repo in _repos)
            {
                foreach (var label in DotNetEpicsConstants.Labels)
                {
                    var task = GetIssuesAsync(client, repo.Owner, repo.Name, label);
                    issueTasks.Add(task);
                }
            }

            await Task.WhenAll(issueTasks);

            // Now parse all issue bodies to find children

            var rootIssues = issueTasks.SelectMany(t => t.Result).ToArray();
            var issueQueue = new Queue<GitHubIssue>(rootIssues);
            var issueById = rootIssues.ToDictionary(i => i.Id);
            var issueChildren = new Dictionary<GitHubIssue, List<GitHubIssue>>();

            while (issueQueue.Count > 0)
            {
                var issue = issueQueue.Dequeue();
                Console.WriteLine($"Processing {issue.Id}...");

                var links = ParseIssueLinks(issue.Id.Owner, issue.Id.Repo, issue.DescriptionMarkdown);
                var children = new List<GitHubIssue>();
                issueChildren.Add(issue, children);

                foreach (var link in links)
                {
                    if (!issueById.TryGetValue(link, out var linkedIssue))
                    {
                        linkedIssue = await GetIssueAsync(client, link);
                        issueById.Add(link, linkedIssue);
                        issueQueue.Enqueue(linkedIssue);
                    }

                    children.Add(linkedIssue);
                }
            }

            // Now build the tree out of that

            var nodeByIssue = issueById.Values.ToDictionary(i => i, i => new GitHubIssueNode { Issue = i });

            foreach (var node in nodeByIssue.Values.OrderBy(n => n.Issue.Id))
            {
                foreach (var linkedIssue in issueChildren[node.Issue])
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

            // Associate project status with issues

            var cards = await cardsTask;

            foreach (var card in cards)
            {
                if (issueById.TryGetValue(card.Id, out var issue))
                    issue.ProjectStatus = card.Status;
            }

            // Fix issue titles

            var regex = string.Join("|", DotNetEpicsConstants.Labels.Select(l => $" *^\\[?{l}\\]? *:? *"));

            foreach (var issue in issueById.Values)
            {
                var match = Regex.Match(issue.Title, regex);
                if (match.Success)
                    issue.Title = issue.Title.Substring(match.Length).Trim();
            }

            var roots = nodeByIssue.Values.Where(n => n.Parent == null);
            var assignees = new SortedSet<string>(roots.SelectMany(r => r.DescendantsAndSelf()).SelectMany(n => n.Issue.Assignees));
            var milestones = new SortedSet<string>(roots.SelectMany(r => r.DescendantsAndSelf()).Select(n => n.Issue.Milestone));
            var releases = new SortedSet<string>(roots.SelectMany(r => r.DescendantsAndSelf()).Select(n => n.Issue.ProjectStatus?.ProjectName));
            var states = new SortedSet<string>(roots.SelectMany(r => r.DescendantsAndSelf()).Select(n => n.Issue.ProjectStatus?.Column));

            var tree = new GitHubIssueTree();
            tree.Roots.AddRange(roots);
            tree.Assignees = assignees;
            tree.Milestones = milestones;
            tree.Releases = releases;
            tree.States = states;
            return tree;
        }

        private async Task<IReadOnlyList<GitHubIssueCard>> GetIssueCards(GitHubClient client)
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

        private static async Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(GitHubClient client, string owner, string repo, string label)
        {
            var issueRequest = new RepositoryIssueRequest();
            issueRequest.State = ItemStateFilter.All;
            issueRequest.Labels.Add(label);
            var issues = await client.Issue.GetAllForRepository(owner, repo, issueRequest);

            var result = new List<GitHubIssue>();

            foreach (var issue in issues)
            {
                var gitHubIssue = CreateGitHubIssue(owner, repo, issue);
                result.Add(gitHubIssue);
            }

            return result;
        }

        private static async Task<GitHubIssue> GetIssueAsync(GitHubClient client, GitHubIssueId id)
        {
            var issue = await client.Issue.Get(id.Owner, id.Repo, id.Number);
            return CreateGitHubIssue(id.Owner, id.Repo, issue);
        }

        private static GitHubIssue CreateGitHubIssue(string owner, string repo, Issue issue)
        {
            var result = new GitHubIssue
            {
                Id = new GitHubIssueId(owner, repo, issue.Number),
                CreatedAt = issue.CreatedAt,
                CreatedBy = issue.User.Login,
                IsClosed = issue.ClosedAt != null,
                Title = issue.Title,
                DescriptionMarkdown = issue.Body,
                Assignees = issue.Assignees.Select(a => a.Login).ToArray(),
                Milestone = issue.Milestone?.Title,
                Labels = issue.Labels.Select(l => CreateGitHubLabel(l)).ToArray()
            };

            bool SetKindWhenContainsLabel(GitHubIssueKind kind, string labelName, bool force = false)
            {
                if (force || result.Labels.Any(l => string.Equals(l.Name, labelName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Kind = kind;
                    return true;
                }

                return false;
            }

            if (!SetKindWhenContainsLabel(GitHubIssueKind.Theme, DotNetEpicsConstants.LabelTheme))
            {
                if (!SetKindWhenContainsLabel(GitHubIssueKind.Epic, DotNetEpicsConstants.LabelEpic))
                {
                    if (!SetKindWhenContainsLabel(GitHubIssueKind.UserStory, DotNetEpicsConstants.LabelUserStory))
                    {
                        SetKindWhenContainsLabel(GitHubIssueKind.Issue, DotNetEpicsConstants.LabelIssue, force: true);
                    }
                }
            }

            return result;
        }

        private static GitHubLabel CreateGitHubLabel(Label label)
        {
            var result = new GitHubLabel();
            result.Name = label.Name;
            result.BackgroundColor = label.Color;
            return result;
        }

        private static IReadOnlyList<GitHubIssueId> ParseIssueLinks(string owner, string repo, string markdown)
        {
            var pipeline = new MarkdownPipelineBuilder()
                .UseTaskLists()
                .UseAutoLinks()
                .Build();

            var document = MarkdownParser.Parse(markdown, pipeline);
            var taskLinkItems = document.Descendants<TaskList>().Select(t => t.Parent);
            var result = new List<GitHubIssueId>();

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
                    result.Add(id.Value);
            }

            return result;
        }
    }
}
