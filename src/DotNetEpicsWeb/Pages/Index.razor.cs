using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Blazored.LocalStorage;

using DotNetEpicsWeb.Data;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;

namespace DotNetEpicsWeb.Pages
{
    public sealed class PageTree
    {
        public PageTree(Tree tree)
        {
            Tree = tree;
        }

        public Tree Tree { get; }
        public List<PageNode> Roots { get; } = new List<PageNode>();
    }

    public sealed class PageNode
    {
        public PageNode(TreeNode treeNode)
        {
            TreeNode = treeNode;
        }

        public TreeNode TreeNode { get; }
        public List<PageNode> Children { get; } = new List<PageNode>();
    }

    public partial class Index
    {
        private readonly Dictionary<string, bool> _nodeStates = new Dictionary<string, bool>();
        private readonly FilterString _defaultFilter;

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

        public Index()
        {
            _defaultFilter = BuildFilterString();
        }

        [Inject]
        public AuthenticationStateProvider AuthenticationStateProvider { get; set; }

        [Inject]
        public ILocalStorageService LocalStorageService { get; set; }

        [Inject]
        public IJSRuntime JSRuntime { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        [Inject]
        public GitHubTreeManager TreeManager { get; set; }

        public PageTree PageTree { get; set; }

        public Tree Tree => PageTree?.Tree;

        public bool CanSeePrivateIssues { get; set; }

        public bool ShowOpen
        {
            get => _showOpen;
            set
            {
                _showOpen = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                _filter = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public bool IncludeThemes
        {
            get => _includeThemes;
            set
            {
                _includeThemes = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public bool IncludeEpics
        {
            get => _includeEpics;
            set
            {
                _includeEpics = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public bool IncludeUserStories
        {
            get => _includeUserStories;
            set
            {
                _includeUserStories = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public bool IncludeIssues
        {
            get => _includeIssues;
            set
            {
                _includeIssues = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string SelectedRelease
        {
            get => _selectedRelease;
            set
            {
                _selectedRelease = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string SelectedState
        {
            get => _selectedState;
            set
            {
                _selectedState = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string SelectedAssignee
        {
            get => _selectedAssignee;
            set
            {
                _selectedAssignee = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string SelectedMilestone
        {
            get => _selectedMilestone;
            set
            {
                _selectedMilestone = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        protected override async Task OnInitializedAsync()
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            CanSeePrivateIssues = state.User.IsInRole(DotNetEpicsConstants.ProductTeamRole);

            await LoadCollapsedIds();

            TreeManager.Changed += TreeChanged;

            var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);

            if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("q", out var q))
            {
                var filterString = FilterString.Parse(q);
                var kinds = filterString.GetValue("kinds") ?? _defaultFilter.GetValue("kinds");
                _filter = filterString.ClearKeys().ToString();
                _showOpen = filterString.GetValues("is").Any(v => string.Equals(v, "open", StringComparison.OrdinalIgnoreCase));
                _includeThemes = kinds.Contains('t');
                _includeEpics = kinds.Contains('e');
                _includeUserStories = kinds.Contains('u');
                _includeIssues = kinds.Contains('i');
                _selectedRelease = filterString.GetValue("release");
                _selectedState = filterString.GetValue("state");
                _selectedAssignee = filterString.GetValue("assignee");
                _selectedMilestone = filterString.GetValue("milestone");
            }

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

        public IEnumerable<string> GetAllIssueIds()
        {
            if (PageTree == null)
                return Enumerable.Empty<string>();

            return PageTree.Tree.Roots.SelectMany(r => r.DescendantsAndSelf())
                                      .Select(n => n.Id);
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
                RebuildNodes(pageTree.Roots, pageTree.Tree.Roots);
                PageTree = pageTree;
            }
        }

        private void RebuildNodes(List<PageNode> pageNodes, IEnumerable<TreeNode> treeNodes)
        {
            foreach (var issueNode in treeNodes)
            {
                if (issueNode.IsPrivate && !CanSeePrivateIssues)
                    continue;

                if (!IsVisible(issueNode))
                    continue;

                if (SkipNode(issueNode))
                {
                    RebuildNodes(pageNodes, issueNode.Children);
                }
                else
                {
                    var node = new PageNode(issueNode);
                    pageNodes.Add(node);
                    RebuildNodes(node.Children, issueNode.Children);
                }
            }
        }

        private void UpdateUrl()
        {
            var filterString = BuildFilterString();

            var queryString = filterString != _defaultFilter
                ? "?q=" + filterString
                : "";

            var uri = new UriBuilder(NavigationManager.Uri)
            {
                Query = queryString
            }.ToString();

            JSRuntime.InvokeAsync<object>("changeUrl", uri);
        }

        private FilterString BuildFilterString()
        {
            var filterString = FilterString.Parse(Filter ?? "");

            if (ShowOpen)
                filterString = filterString.SetValue("is", "open");
            else
                filterString = filterString.SetValue("is", "closed");

            var kinds = "";
            if (IncludeThemes)
                kinds += "t";
            if (IncludeEpics)
                kinds += "e";
            if (IncludeUserStories)
                kinds += "u";
            if (IncludeIssues)
                kinds += "i";

            filterString = filterString.SetValue("kinds", kinds);

            if (SelectedRelease != null)
                filterString = filterString.SetValue("release", SelectedRelease);

            if (SelectedState != null)
                filterString = filterString.SetValue("state", SelectedState);

            if (SelectedAssignee != null)
                filterString = filterString.SetValue("assignee", SelectedAssignee);

            if (SelectedMilestone != null)
                filterString = filterString.SetValue("milestone", SelectedMilestone);
            return filterString;
        }

        private bool IsVisible(TreeNode node)
        {
            return IsDirectlyVisible(node) ||
                   IsIndirectlyVisible(node);
        }

        private bool IsDirectlyVisible(TreeNode node)
        {
            if (ShowOpen && node.IsClosed || !ShowOpen && !node.IsClosed)
                return false;

            if (SelectedRelease != null && SelectedRelease != (node.ReleaseInfo?.Release ?? ""))
                return false;

            if (SelectedState != null && SelectedState != (node.ReleaseInfo?.Status ?? ""))
                return false;

            if (SelectedAssignee != null)
            {
                if (SelectedAssignee == "")
                {
                    if (node.Assignees.Any())
                        return false;
                }
                else if (!node.Assignees.Contains(SelectedAssignee))
                {
                    return false;
                }
            }

            if (SelectedMilestone != null && SelectedMilestone != (node.Milestone ?? ""))
                return false;

            var filters = FilterString.Parse(Filter)
                                      .Where(t => string.IsNullOrEmpty(t.Key) && !string.IsNullOrWhiteSpace(t.Value))
                                      .Select(t => t.Value);

            var hasUnmatchedFilter = false;

            foreach (var f in filters)
            {
                if (node.Title.Contains(f, StringComparison.OrdinalIgnoreCase))
                    continue;
                else if (node.Id.ToString().Contains(f, StringComparison.OrdinalIgnoreCase))
                    continue;
                else if (node.Assignees.Any(a => a.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    continue;
                else if (node.Milestone != null && node.Milestone.Contains(f, StringComparison.OrdinalIgnoreCase))
                    continue;
                else if (node.ReleaseInfo?.Release != null && node.ReleaseInfo.Release.Contains(f, StringComparison.OrdinalIgnoreCase))
                    continue;
                else if (node.ReleaseInfo?.Status != null && node.ReleaseInfo.Status.Contains(f, StringComparison.OrdinalIgnoreCase))
                    continue;
                else if (node.Labels.Any(l => l.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    continue;

                hasUnmatchedFilter = true;
                break;
            }

            return !hasUnmatchedFilter;
        }

        private bool IsIndirectlyVisible(TreeNode node)
        {
            return !IsDirectlyVisible(node) && node.Descendants().Any(n => !SkipNode(n) && IsDirectlyVisible(n));
        }

        private bool SkipNode(TreeNode node)
        {
            if (!IncludeThemes && node.Kind == TreeNodeKind.Theme)
                return true;

            if (!IncludeEpics && node.Kind == TreeNodeKind.Epic)
                return true;

            if (!IncludeUserStories && node.Kind == TreeNodeKind.UserStory)
                return true;

            if (!IncludeIssues && node.Kind == TreeNodeKind.Issue)
                return true;

            return false;
        }

        public bool IsMuted(PageNode node)
        {
            return IsIndirectlyVisible(node.TreeNode);
        }

        public bool IsExpanded(PageNode node)
        {
            if (_nodeStates.TryGetValue(node.TreeNode.Id, out var state))
                return state;

            return true;
        }

        public void ToggleNode(PageNode node)
        {
            _nodeStates[node.TreeNode.Id] = !IsExpanded(node);

            SaveCollapsedIds();
        }

        public void ExpandAll()
        {
            foreach (var issueId in GetAllIssueIds())
                _nodeStates[issueId] = true;

            SaveCollapsedIds();
        }

        public void CollapseAll()
        {
            foreach (var issueId in GetAllIssueIds())
                _nodeStates[issueId] = false;

            SaveCollapsedIds();
        }

        private async Task LoadCollapsedIds()
        {
            var collapsedIds = await LocalStorageService.GetItemAsync<string[]>("collapsedIds") ?? Array.Empty<string>();
            foreach (var id in collapsedIds)
                _nodeStates[id] = false;
        }

        private void SaveCollapsedIds()
        {
            var collapsedIds = _nodeStates.Where(kv => kv.Value == false)
                                          .Select(kv => kv.Key.ToString())
                                          .ToArray();
            LocalStorageService.SetItemAsync("collapsedIds", collapsedIds);
        }
    }
}
