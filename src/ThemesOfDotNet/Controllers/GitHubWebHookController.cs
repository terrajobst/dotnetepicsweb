using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using ThemesOfDotNet.Data;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ThemesOfDotNet.Controllers
{
    [ApiController]
    [Route("github-webhook")]
    [AllowAnonymous]
    public sealed class GitHubWebHookController : Controller
    {
        private static readonly HashSet<string> _relevantActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "opened",
            "edited",
            "deleted",
            "closed",
            "reopened",
            "assigned",
            "unassigned",
            "labeled",
            "unlabeled",
            "transferred",
            "milestoned",
            "demilestoned",
            "created",
            "moved"
        };

        private static readonly HashSet<string> _relevantLabels = new HashSet<string>(ThemesOfDotNetConstants.Labels, StringComparer.OrdinalIgnoreCase);

        private readonly ILogger<GitHubWebHookController> _logger;
        private readonly TreeService _treeService;

        public GitHubWebHookController(ILogger<GitHubWebHookController> logger, TreeService treeService)
        {
            _logger = logger;
            _treeService = treeService;
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            if (Request.ContentType != MediaTypeNames.Application.Json)
                return BadRequest();

            // Get payload
            string payload;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                payload = await reader.ReadToEndAsync();

            WebHookPayload typedPayload;

            try
            {
                typedPayload = JsonSerializer.Deserialize<WebHookPayload>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Can't deserialize GitHub web hook: {error}", ex.Message);
                return BadRequest();
            }

            var knownIds = GetKnownIds().ToHashSet();

            var isRelevant = IsRelevant(typedPayload, knownIds);
            var payloadResult = new { IsRelevant = isRelevant, Payload = payload };
            _logger.LogInformation("Processed GitHub web hook: {payloadResult}", payloadResult);

            if (isRelevant)
            {
                // Don't await. Just kick of the work here so we don't time out.
                _ = _treeService.InvalidateAsync();
            }

            return Ok(isRelevant);
        }

        private IEnumerable<GitHubIssueId> GetKnownIds()
        {
            var tree = _treeService.Tree;
            if (tree != null)
            {
                foreach (var node in tree.Roots.SelectMany(r => r.DescendantsAndSelf()))
                {
                    if (GitHubIssueId.TryParse(node.Id, out var id))
                        yield return id;
                }
            }
        }

        private static bool IsRelevant(WebHookPayload payload, HashSet<GitHubIssueId> knownIds)
        {
            if (payload.action == null || !_relevantActions.Contains(payload.action))
                return false;

            if (payload.issue != null && payload.repository != null)
            {
                var idText = payload.repository.full_name + "#" + payload.issue.number;
                if (GitHubIssueId.TryParse(idText, out var issueId) && knownIds.Contains(issueId))
                    return true;
            }

            if (payload.project_card != null)
            {
                if (GitHubIssueId.TryParse(payload.project_card.content_url, out var issueId) && knownIds.Contains(issueId))
                    return true;
            }

            return IsRelevant(payload.label) ||
                   IsRelevant(payload.issue);
        }

        private static bool IsRelevant(Issue payload)
        {
            if (payload == null || payload.labels == null)
                return false;

            return IsRelevant(payload.labels);
        }

        private static bool IsRelevant(Label[] payload)
        {
            if (payload == null || payload.Length == 0)
                return false;

            foreach (var label in payload)
                if (IsRelevant(label))
                    return true;

            return false;
        }

        private static bool IsRelevant(Label payload)
        {
            if (payload == null || payload.name == null)
                return false;

            return _relevantLabels.Contains(payload.name);
        }

        private class WebHookPayload
        {
            public string action { get; set; }
            public Issue issue { get; set; }
            public Repository repository { get; set; }
            public Label label { get; set; }
            public Card project_card { get; set; }
        }

        private class Repository
        {
            public string full_name { get; set; }
        }

        private class Issue
        {
            public int number { get; set; }
            public Label[] labels { get; set; }
        }

        private class Card
        {
            public string content_url { get; set; }
        }

        private class Label
        {
            public string name { get; set; }
        }
    }
}
