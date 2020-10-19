namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubProjectStatus
    {
        public string ProjectName { get; set; }
        public string Column { get; set; }

        public override string ToString()
        {
            return $"{ProjectName} ({Column})";
        }
    }
}
