using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using GitHubJwt;

using Microsoft.Extensions.Configuration;

using Octokit;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubClientFactory
    {
        private readonly IConfiguration _configuration;

        public GitHubClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<GitHubClient> CreateAsync()
        {
            // See: https://octokitnet.readthedocs.io/en/latest/github-apps/ for details.

            var appId = Convert.ToInt32(_configuration["GitHubAppId"]);
            var privateKey = _configuration["GitHubAppPrivateKey"];

            var privateKeySource = new PlainStringPrivateKeySource(privateKey);
            var generator = new GitHubJwtFactory(
                privateKeySource,
                new GitHubJwtFactoryOptions
                {
                    AppIntegrationId = appId,
                    ExpirationSeconds = 8 * 60 // 600 is apparently too high
                });
            var token = generator.CreateEncodedJwtToken();

            var client = CreateForToken(token, AuthenticationType.Bearer);

            var installations = await client.GitHubApps.GetAllInstallationsForCurrent();
            var installation = installations.Single();
            var installationTokenResult = await client.GitHubApps.CreateInstallationToken(installation.Id);

            return CreateForToken(installationTokenResult.Token, AuthenticationType.Oauth);
        }

        private static GitHubClient CreateForToken(string token, AuthenticationType authenticationType)
        {
            var productInformation = new ProductHeaderValue(DotNetEpicsConstants.ApplicationName);
            var client = new GitHubClient(productInformation)
            {
                Credentials = new Credentials(token, authenticationType)
            };
            return client;
        }

        public sealed class PlainStringPrivateKeySource : IPrivateKeySource
        {
            private readonly string _key;

            public PlainStringPrivateKeySource(string key)
            {
                _key = key;
            }

            public TextReader GetPrivateKeyReader()
            {
                return new StringReader(_key);
            }
        }
    }
}
