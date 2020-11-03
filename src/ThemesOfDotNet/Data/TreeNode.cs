using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using Humanizer;

namespace ThemesOfDotNet.Data
{
    public sealed class TreeNode
    {
        public string Id { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsBottomUp { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public bool IsClosed { get; set; }
        public string Title { get; set; }
        public string Milestone { get; set; }
        public int? Priority { get; set; }
        public TreeNodeCost? Cost { get; set; }
        public IReadOnlyList<string> Assignees { get; set; } = Array.Empty<string>();
        public IReadOnlyList<TreeNodeLabel> Labels { get; set; } = Array.Empty<TreeNodeLabel>();
        public TreeNodeKind Kind { get; set; }
        public TreeNodeStatus ReleaseInfo { get; set; }
        public string Url { get; set; }
        public List<TreeNode> Children { get; set; } = new List<TreeNode>();

        [JsonIgnore]
        public string DetailText => $"{Id} opened {CreatedAt.Humanize()}";

        public IEnumerable<TreeNode> DescendantsAndSelf()
        {
            var stack = new Stack<TreeNode>();
            stack.Push(this);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;

                foreach (var child in node.Children.AsEnumerable().Reverse())
                    stack.Push(child);
            }
        }

        public IEnumerable<TreeNode> Descendants()
        {
            return DescendantsAndSelf().Skip(1);
        }
    }
}
