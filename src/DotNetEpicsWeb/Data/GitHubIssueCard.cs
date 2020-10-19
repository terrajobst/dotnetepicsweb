namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubIssueCard
    {
        public GitHubIssueCard(GitHubIssueId id, string projectName, string column)
        {
            Id = id;
            Status = new GitHubProjectStatus
            {
                ProjectName = projectName,
                Column = column
            };
        }

        public GitHubIssueId Id { get; }
        public GitHubProjectStatus Status { get; }

        public override string ToString()
        {
            return $"{Id} - {Status}";
        }
    }
}
