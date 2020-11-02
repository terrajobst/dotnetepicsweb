using System;
using System.Linq;
using System.Threading.Tasks;

using Octokit.GraphQL;

namespace ThemesOfDotNet.Data
{
    internal static class GitHubAuthHelpers
    {
        public static async Task<bool> IsMemberOfTeamAsync(string accessToken, string orgName, string teamName, string userName)
        {
            try
            {
                var productInformation = new ProductHeaderValue(ThemesOfDotNetConstants.ApplicationName);
                var connection = new Connection(productInformation, accessToken);

                var query = new Query()
                    .Organization(orgName)
                    .Team(teamName)
                    .Members(null, null, null, null, null, null, userName, null)
                    .AllPages()
                    .Select(u => u.Login);

                var result = await connection.Run(query);
                var exactUser = result.FirstOrDefault(login => string.Equals(login, userName, StringComparison.OrdinalIgnoreCase));
                return exactUser != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
