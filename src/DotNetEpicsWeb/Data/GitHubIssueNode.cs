using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubIssueNode
    {
        [JsonIgnore]
        public GitHubIssueNode Parent { get; set; }
        public GitHubIssue Issue { get; set; }
        public List<GitHubIssueNode> Children { get; set; } = new List<GitHubIssueNode>();

        public IEnumerable<GitHubIssueNode> DescendantsAndSelf()
        {
            var stack = new Stack<GitHubIssueNode>();
            stack.Push(this);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;

                foreach (var child in node.Children.AsEnumerable().Reverse())
                    stack.Push(child);
            }
        }

        public IEnumerable<GitHubIssueNode> Descendants()
        {
            return DescendantsAndSelf().Skip(1);
        }
    }
}
