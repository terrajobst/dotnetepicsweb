using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Blazored.LocalStorage;

using ThemesOfDotNet.Data;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using System.IO.Compression;
using System.Text.Json;
using System.IO;
using System.Text;

namespace ThemesOfDotNet.Pages
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
        private bool _includeUserStories = true;
        private bool _includeIssues;
        private bool _includeBottomUp;
        private string _selectedRelease;
        private string _selectedState;
        private string _selectedAssignee;
        private string _selectedMilestone;
        private string _selectedPriority;
        private string _selectedCost;
        private string _selectedTeam;

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
        public IWebHostEnvironment Environment { get; set; }

        [Inject]
        public TreeService TreeService { get; set; }

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

        public bool IncludeBottomUp
        {
            get => _includeBottomUp;
            set
            {
                _includeBottomUp = value;
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

        public string SelectedPriority
        {
            get => _selectedPriority;
            set
            {
                _selectedPriority = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string SelectedCost
        {
            get => _selectedCost;
            set
            {
                _selectedCost = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        public string SelectedTeam
        {
            get => _selectedTeam;
            set
            {
                _selectedTeam = value;
                RebuildPageTree();
                UpdateUrl();
            }
        }

        protected override async Task OnInitializedAsync()
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            CanSeePrivateIssues = state.User.IsInRole(ThemesOfDotNetConstants.ProductTeamRole);

            TreeService.Changed += TreeChanged;

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
                _includeBottomUp = kinds.Contains('b');
                _selectedRelease = filterString.GetValue("release");
                _selectedState = filterString.GetValue("state");
                _selectedAssignee = filterString.GetValue("assignee");
                _selectedMilestone = filterString.GetValue("milestone");
                _selectedPriority = filterString.GetValue("priority");
                _selectedCost = filterString.GetValue("cost");
                _selectedTeam = filterString.GetValue("team");
            }

            RebuildPageTree();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await LoadOpenIds();
                await ChangeTree();
            }

            await base.OnAfterRenderAsync(firstRender);
        }

        public void Dispose()
        {
            TreeService.Changed -= TreeChanged;
        }

        private async void TreeChanged(object sender, EventArgs e)
        {
            await ChangeTree();
        }

        private Task ChangeTree()
        {
            return InvokeAsync(() =>
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
            if (TreeService.Tree == null)
            {
                PageTree = null;
            }
            else
            {
                var pageTree = new PageTree(TreeService.Tree);
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

                if (!IncludeBottomUp && issueNode.IsBottomUp)
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
            if (IncludeBottomUp)
                kinds += "b";

            filterString = filterString.SetValue("kinds", kinds);

            if (SelectedRelease != null)
                filterString = filterString.SetValue("release", SelectedRelease);

            if (SelectedState != null)
                filterString = filterString.SetValue("state", SelectedState);

            if (SelectedAssignee != null)
                filterString = filterString.SetValue("assignee", SelectedAssignee);

            if (SelectedMilestone != null)
                filterString = filterString.SetValue("milestone", SelectedMilestone);

            if (SelectedPriority != null)
                filterString = filterString.SetValue("priority", SelectedPriority);

            if (SelectedCost != null)
                filterString = filterString.SetValue("cost", SelectedCost);

            if (SelectedTeam != null)
                filterString = filterString.SetValue("team", SelectedTeam);

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

            if (SelectedPriority != null && SelectedPriority != (node.Priority?.ToString() ?? ""))
                return false;

            if (SelectedCost != null && SelectedCost != (node.Cost?.ToString() ?? ""))
                return false;

            if (SelectedTeam != null)
            {
                if (SelectedTeam == "")
                {
                    if (node.Teams.Any())
                        return false;
                }
                else if (!node.Teams.Contains(SelectedTeam))
                {
                    return false;
                }
            }

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
            if (IsDirectlyVisible(node))
                return false;

            return node.Descendants().Any(n => !SkipNode(n) && IsDirectlyVisible(n)) ||
                   !(ShowOpen && node.IsClosed) && node.Ancestors().Any(n => !SkipNode(n) && IsDirectlyVisible(n));
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

            return false;
        }

        public void ToggleNode(PageNode node)
        {
            _nodeStates[node.TreeNode.Id] = !IsExpanded(node);

            SaveOpenIds();
        }

        public void ExpandAll()
        {
            foreach (var issueId in GetAllIssueIds())
                _nodeStates[issueId] = true;

            SaveOpenIds();
        }

        public void CollapseAll()
        {
            foreach (var issueId in GetAllIssueIds())
                _nodeStates[issueId] = false;

            SaveOpenIds();
        }

        private async Task LoadOpenIds()
        {
            var openIds = Array.Empty<string>();
            try
            {
                var base64 = await LocalStorageService.GetItemAsync<string>("openIds");
                var input = new MemoryStream(Convert.FromBase64String(base64));
                var output = new MemoryStream();
                using (var bzStream = new BrotliStream(input, CompressionMode.Decompress))
                {
                    bzStream.CopyTo(output);
                }
                output.Position = 0;
                var csv = Encoding.UTF8.GetString(output.ToArray());
                openIds = csv.Split(',');
            }
            catch (Exception)
            {
                // Previous format
                openIds = await LocalStorageService.GetItemAsync<string[]>("openIds") ?? Array.Empty<string>();
            }

            foreach (var id in openIds)
            {
                _nodeStates[id] = true;
            }
        }

        private void SaveOpenIds()
        {
            var openIds = _nodeStates.Where(kv => kv.Value == true)
                                          .Select(kv => kv.Key.ToString())
                                          .ToArray();

            // Save storage as base64 encoded brotli compressed csv; which saves 86% vs Json
            var csv = string.Join(',', openIds);
            var input = Encoding.UTF8.GetBytes(csv);
            var output = new MemoryStream();
            using (var bzStream = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                bzStream.Write(input);
            }
            output.Position = 0;
            var base64 = Convert.ToBase64String(output.ToArray());

            LocalStorageService.SetItemAsync("openIds", base64);
        }
    }
}
