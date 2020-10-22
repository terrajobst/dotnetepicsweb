using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;

using DotNetEpicsWeb.Data;

using Microsoft.AspNetCore.Components;

namespace DotNetEpicsWeb.Pages
{
    public sealed class PageTree
    {
        public PageTree(GitHubIssueTree issueTree)
        {
            IssueTree = issueTree;
        }

        public GitHubIssueTree IssueTree { get; }
        public List<PageNode> Roots { get; } = new List<PageNode>();
    }

    public sealed class PageNode
    {
        public PageNode(GitHubIssueNode issueNode)
        {
            IssueNode = issueNode;
        }

        public GitHubIssueNode IssueNode { get; }
        public List<PageNode> Children { get; } = new List<PageNode>();
    }

    public partial class Index
    {
        private readonly Dictionary<GitHubIssueId, bool> _nodeStates = new Dictionary<GitHubIssueId, bool>();
        private bool _showOpen = true;
        private string _filter;
        private bool _includeThemes = true;
        private bool _includeEpics = true;
        private bool _includeUserStories;
        private bool _includeIssues;
        private string _selectedRelease;
        private string _selectedState;
        private string _selectedAssignee;
        private string _selectedMilestone;

        [Inject]
        public GitHubTreeManager TreeManager { get; set; }

        public PageTree PageTree { get; set; }

        public GitHubIssueTree Tree => PageTree?.IssueTree;

        public bool ShowOpen
        {
            get => _showOpen;
            set
            {
                _showOpen = value;
                RebuildPageTree();
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                _filter = value;
                RebuildPageTree();
            }
        }

        public bool IncludeThemes
        {
            get => _includeThemes;
            set
            {
                _includeThemes = value;
                RebuildPageTree();
            }
        }

        public bool IncludeEpics
        {
            get => _includeEpics;
            set
            {
                _includeEpics = value;
                RebuildPageTree();
            }
        }

        public bool IncludeUserStories
        {
            get => _includeUserStories;
            set
            {
                _includeUserStories = value;
                RebuildPageTree();
            }
        }

        public bool IncludeIssues
        {
            get => _includeIssues;
            set
            {
                _includeIssues = value;
                RebuildPageTree();
            }
        }

        public string SelectedRelease
        {
            get => _selectedRelease;
            set
            {
                _selectedRelease = value;
                RebuildPageTree();
            }
        }

        public string SelectedState
        {
            get => _selectedState;
            set
            {
                _selectedState = value;
                RebuildPageTree();
            }
        }

        public string SelectedAssignee
        {
            get => _selectedAssignee;
            set
            {
                _selectedAssignee = value;
                RebuildPageTree();
            }
        }

        public string SelectedMilestone
        {
            get => _selectedMilestone;
            set
            {
                _selectedMilestone = value;
                RebuildPageTree();
            }
        }

        protected override void OnInitialized()
        {
            TreeManager.Changed += TreeChanged;
            RebuildPageTree();
        }

        public void Dispose()
        {
            TreeManager.Changed -= TreeChanged;
        }

        private async void TreeChanged(object sender, EventArgs e)
        {
            await InvokeAsync(() =>
            {
                RebuildPageTree();

                // Remove state for nodes that are no longer part of the tree

                var nodeIds = GetAllIssueIds().ToHashSet();

                foreach (var id in _nodeStates.Keys.ToArray())
                {
                    if (!nodeIds.Contains(id))
                        _nodeStates.Remove(id);
                }

                StateHasChanged();
            });
        }

        public IEnumerable<GitHubIssueId> GetAllIssueIds()
        {
            if (PageTree == null)
                return Enumerable.Empty<GitHubIssueId>();

            return PageTree.IssueTree.Roots.SelectMany(r => r.DescendantsAndSelf())
                                     .Select(n => n.Issue.Id);
        }

        private void RebuildPageTree()
        {
            if (TreeManager.Tree == null)
            {
                PageTree = null;
            }
            else
            {
                var pageTree = new PageTree(TreeManager.Tree);
                RebuildNodes(pageTree.Roots, pageTree.IssueTree.Roots);
                PageTree = pageTree;
            }
        }

        private void RebuildNodes(List<PageNode> nodes, List<GitHubIssueNode> issueNodes)
        {
            foreach (var issueNode in issueNodes)
            {
                if (!IsVisible(issueNode))
                    continue;

                if (SkipNode(issueNode))
                {
                    RebuildNodes(nodes, issueNode.Children);
                }
                else
                {
                    var node = new PageNode(issueNode);
                    nodes.Add(node);
                    RebuildNodes(node.Children, issueNode.Children);
                }
            }
        }

        private bool IsVisible(GitHubIssueNode node)
        {
            return IsDirectlyVisible(node) ||
                   IsIndirectlyVisible(node);
        }

        private bool IsDirectlyVisible(GitHubIssueNode node)
        {
            var issue = node.Issue;

            if (ShowOpen && issue.IsClosed || !ShowOpen && !issue.IsClosed)
                return false;

            if (SelectedRelease != null && SelectedRelease != (node.Issue.ProjectStatus?.ProjectName ?? ""))
                return false;

            if (SelectedState != null && SelectedState != (node.Issue.ProjectStatus?.Column ?? ""))
                return false;

            if (SelectedAssignee != null)
            {
                if (SelectedAssignee == "")
                {
                    if (node.Issue.Assignees.Any())
                        return false;
                }
                else if (!node.Issue.Assignees.Contains(SelectedAssignee))
                {
                    return false;
                }
            }

            if (SelectedMilestone != null && SelectedMilestone != (node.Issue.Milestone ?? ""))
                return false;

            if (string.IsNullOrEmpty(Filter))
                return true;

            if (issue.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                return true;

            if (issue.Id.ToString().Contains(Filter, StringComparison.OrdinalIgnoreCase))
                return true;

            if (issue.Assignees.Any(a => a.Contains(Filter, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (issue.Milestone != null && issue.Milestone.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                return true;

            if (issue.ProjectStatus != null)
            {
                if (issue.ProjectStatus.ProjectName.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (issue.ProjectStatus.Column.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var label in issue.Labels)
            {
                if (label.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool IsIndirectlyVisible(GitHubIssueNode node)
        {
            return !IsDirectlyVisible(node) && node.Descendants().Any(n => !SkipNode(n) && IsDirectlyVisible(n));
        }

        private bool SkipNode(GitHubIssueNode node)
        {
            if (!IncludeThemes && node.Issue.Kind == GitHubIssueKind.Theme)
                return true;

            if (!IncludeEpics && node.Issue.Kind == GitHubIssueKind.Epic)
                return true;

            if (!IncludeUserStories && node.Issue.Kind == GitHubIssueKind.UserStory)
                return true;

            if (!IncludeIssues && node.Issue.Kind == GitHubIssueKind.Issue)
                return true;

            return false;
        }

        public bool IsMuted(PageNode node)
        {
            return IsIndirectlyVisible(node.IssueNode);
        }

        public bool IsExpanded(PageNode node)
        {
            if (_nodeStates.TryGetValue(node.IssueNode.Issue.Id, out var state))
                return state;

            return true;
        }

        public void ToggleNode(PageNode node)
        {
            _nodeStates[node.IssueNode.Issue.Id] = !IsExpanded(node);
        }

        public void ExpandAll()
        {
            foreach (var issueId in GetAllIssueIds())
                _nodeStates[issueId] = true;
        }

        public void CollapseAll()
        {
            foreach (var issueId in GetAllIssueIds())
                _nodeStates[issueId] = false;
        }
    }
}
