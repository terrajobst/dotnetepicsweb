using System;
using System.Linq;

using DotNetEpicsWeb.Data;

using Microsoft.AspNetCore.Components;

namespace DotNetEpicsWeb.Pages
{
    public partial class Index
    {
        [Inject]
        public GitHubTreeManager TreeManager { get; set; }

        public GitHubIssueTree Tree => TreeManager.Tree;

        public bool ShowOpen { get; set; } = true;

        public string Filter { get; set; }

        public bool IncludeEpics { get; set; } = true;

        public bool IncludeExperiences { get; set; } = true;

        public bool IncludeUserStories { get; set; } = true;

        public bool IncludeIssues { get; set; }

        public string SelectedRelease { get; set; }

        public string SelectedState { get; set; }

        public string SelectedAssignee { get; set; }

        public string SelectedMilestone { get; set; }

        protected override void OnInitialized()
        {
            TreeManager.Changed += TreeChanged;
        }

        public void Dispose()
        {
            TreeManager.Changed -= TreeChanged;
        }

        private async void TreeChanged(object sender, EventArgs e)
        {
            await InvokeAsync(() => StateHasChanged());
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
                if (SelectedAssignee == "" && node.Issue.Assignees.Any())
                    return false;
                else if (!node.Issue.Assignees.Contains(SelectedAssignee))
                    return false;
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
            if (!IncludeEpics && node.Issue.Kind == GitHubIssueKind.Epic)
                return true;

            if (!IncludeExperiences && node.Issue.Kind == GitHubIssueKind.Experience)
                return true;

            if (!IncludeUserStories && node.Issue.Kind == GitHubIssueKind.UserStory)
                return true;

            if (!IncludeIssues && node.Issue.Kind == GitHubIssueKind.Issue)
                return true;

            return false;
        }
    }
}
