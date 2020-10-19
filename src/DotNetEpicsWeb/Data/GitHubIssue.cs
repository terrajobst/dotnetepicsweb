using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubIssue
    {
        public GitHubIssueId Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public bool IsClosed { get; set; }
        public string Title { get; set; }
        public string DescriptionMarkdown { get; set; }
        public string Milestone { get; set; }
        public IReadOnlyList<string> Assignees { get; set; }
        public IReadOnlyList<GitHubLabel> Labels { get; set; }

        public GitHubIssueKind Kind { get; set; }
        public GitHubProjectStatus ProjectStatus { get; set; }

        [JsonIgnore]
        public string Url => $"https://github.com/{Id.Owner}/{Id.Repo}/issues/{Id.Number}";

        [JsonIgnore]
        public string DetailText => $"{Id} opened {CreatedAt.FormatRelative()}";
    }
}
