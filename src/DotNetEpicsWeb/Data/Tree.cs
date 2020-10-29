using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetEpicsWeb.Data
{
    public sealed class Tree
    {
        public static Tree Empty { get; } = new Tree(Array.Empty<TreeNode>());

        public Tree(IEnumerable<TreeNode> roots)
        {
            Roots = roots.ToArray();
            var allNodes = Roots.SelectMany(r => r.DescendantsAndSelf());
            Assignees = new SortedSet<string>(allNodes.SelectMany(n => n.Assignees)) { null };
            Milestones = new SortedSet<string>(allNodes.Select(n => n.Milestone)) { null };
            Releases = new SortedSet<string>(allNodes.Select(n => n.ReleaseInfo?.Release)) { null };
            States = new SortedSet<string>(allNodes.Select(n => n.ReleaseInfo?.Status)) { null };
        }

        public IReadOnlyCollection<TreeNode> Roots { get; }
        public IReadOnlyCollection<string> Assignees { get; }
        public IReadOnlyCollection<string> Milestones { get; }
        public IReadOnlyCollection<string> Releases { get; }
        public IReadOnlyCollection<string> States { get; }
    }
}
