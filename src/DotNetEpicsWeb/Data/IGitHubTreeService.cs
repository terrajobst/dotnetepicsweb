using System.Threading.Tasks;

namespace DotNetEpicsWeb.Data
{
    public interface IGitHubTreeService
    {
        public Task<GitHubIssueTree> GetIssueTreeAsync();
    }
}
