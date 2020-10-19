using System.Collections.Generic;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubIssueTree
    {
        public List<GitHubIssueNode> Roots { get; set; } = new List<GitHubIssueNode>();
        public IReadOnlyCollection<string> Assignees { get; set; }
        public IReadOnlyCollection<string> Milestones { get; set; }
        public IReadOnlyCollection<string> Releases { get; set; }
        public IReadOnlyCollection<string> States { get; set; }
    }
}
