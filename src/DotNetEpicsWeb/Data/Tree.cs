using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace DotNetEpicsWeb.Data
{
    public sealed class Tree
    {
        private IReadOnlyCollection<TreeNode> _roots;

        public static Tree Empty { get; } = new Tree(Array.Empty<TreeNode>());

        public Tree()
            : this(Enumerable.Empty<TreeNode>())
        {
        }

        public Tree(IEnumerable<TreeNode> roots)
        {
            Roots = roots.ToArray();
        }

        public IReadOnlyCollection<TreeNode> Roots
        {
            get => _roots;
            set
            {
                _roots = value;
                var allNodes = Roots?.SelectMany(r => r.DescendantsAndSelf()) ?? Array.Empty<TreeNode>();
                Assignees = new SortedSet<string>(allNodes.SelectMany(n => n.Assignees)) { null };
                Milestones = new SortedSet<string>(allNodes.Select(n => n.Milestone)) { null };
                Releases = new SortedSet<string>(allNodes.Select(n => n.ReleaseInfo?.Release)) { null };
                States = new SortedSet<string>(allNodes.Select(n => n.ReleaseInfo?.Status)) { null };
                Priorities = new SortedSet<int?>(allNodes.Select(n => n.Priority)) { null };
                Costs = new SortedSet<TreeNodeCost?>(allNodes.Select(n => n.Cost)) { null };
            }
        }

        [JsonIgnore]
        public IReadOnlyCollection<string> Assignees { get; private set; }

        [JsonIgnore]
        public IReadOnlyCollection<string> Milestones { get; private set; }

        [JsonIgnore]
        public IReadOnlyCollection<string> Releases { get; private set; }

        [JsonIgnore]
        public IReadOnlyCollection<string> States { get; private set; }

        [JsonIgnore]
        public IReadOnlyCollection<int?> Priorities { get; private set; }

        [JsonIgnore]
        public IReadOnlyCollection<TreeNodeCost?> Costs { get; private set; }
    }
}
