using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ThemesOfDotNet.Data
{
    public sealed class Tree
    {
        public static Tree Empty { get; } = new Tree(Array.Empty<TreeNode>());

        public Tree()
            : this(Enumerable.Empty<TreeNode>())
        {
        }

        public Tree(IEnumerable<TreeNode> roots)
        {
            Roots = roots.ToArray();
            Initialize();
        }

        public void Initialize()
        {
            var allNodes = Roots?.SelectMany(r => r.DescendantsAndSelf()) ?? Array.Empty<TreeNode>();
            Assignees = new SortedSet<string>(allNodes.SelectMany(n => n.Assignees)) { null };
            Milestones = new SortedSet<string>(allNodes.Select(n => n.Milestone)) { null };
            Releases = new SortedSet<string>(allNodes.Select(n => n.ReleaseInfo?.Release)) { null };
            States = new SortedSet<string>(allNodes.Select(n => n.ReleaseInfo?.Status)) { null };
            Priorities = new SortedSet<int?>(allNodes.Select(n => n.Priority)) { null };
            Costs = new SortedSet<TreeNodeCost?>(allNodes.Select(n => n.Cost)) { null };
            Teams = new SortedSet<string>(allNodes.SelectMany(n => n.Teams)) { null };

            foreach (var parent in allNodes)
            {
                foreach (var child in parent.Children)
                    child.Parents.Add(parent);

                parent.Children.Sort((x, y) =>
                {
                    var result = x.Kind.CompareTo(y.Kind);
                    if (result != 0)
                        return result;

                    if (x.Priority != null || y.Priority != null)
                    {
                        if (x.Priority == null)
                            return 1;

                        if (y.Priority == null)
                            return -1;

                        result = x.Priority.Value.CompareTo(y.Priority.Value);
                        if (result != 0)
                            return result;
                    }

                    if (x.Cost != null || y.Cost != null)
                    {
                        if (x.Cost == null)
                            return 1;

                        if (y.Cost == null)
                            return -1;

                        result = -x.Cost.Value.CompareTo(y.Cost.Value);
                        if (result != 0)
                            return result;
                    }

                    return x.Title.CompareTo(y.Title);
                });
            }           
        }

        public IReadOnlyCollection<TreeNode> Roots { get; set; }

        [JsonIgnore]
        public IReadOnlyCollection<string> Assignees { get; private set; } = Array.Empty<string>();

        [JsonIgnore]
        public IReadOnlyCollection<string> Milestones { get; private set; } = Array.Empty<string>();

        [JsonIgnore]
        public IReadOnlyCollection<string> Releases { get; private set; } = Array.Empty<string>();

        [JsonIgnore]
        public IReadOnlyCollection<string> States { get; private set; } = Array.Empty<string>();

        [JsonIgnore]
        public IReadOnlyCollection<int?> Priorities { get; private set; } = Array.Empty<int?>();

        [JsonIgnore]
        public IReadOnlyCollection<TreeNodeCost?> Costs { get; private set; } = Array.Empty<TreeNodeCost?>();

        [JsonIgnore]
        public IReadOnlyCollection<string> Teams { get; private set; } = Array.Empty<string>();
    }
}
