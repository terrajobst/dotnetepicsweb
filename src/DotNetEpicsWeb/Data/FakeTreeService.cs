using System.Threading.Tasks;

namespace DotNetEpicsWeb.Data
{
    public sealed class FakeTreeService : IGitHubTreeService
    {
        private readonly GitHubTreePersistence _persistence;

        public FakeTreeService(GitHubTreePersistence persistence)
        {
            _persistence = persistence;
        }

        public Task<GitHubIssueTree> GetIssueTreeAsync()
        {
            return _persistence.LoadAsync();
        }
    }
}
