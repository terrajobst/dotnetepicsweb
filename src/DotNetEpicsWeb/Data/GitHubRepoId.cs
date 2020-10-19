using System;
using System.Diagnostics.CodeAnalysis;

namespace DotNetEpicsWeb.Data
{
    public struct GitHubRepoId : IEquatable<GitHubRepoId>, IComparable, IComparable<GitHubRepoId>
    {
        public GitHubRepoId(string owner, string name)
        {
            Owner = owner;
            Name = name;
        }

        public string Owner { get; }
        public string Name { get; }

        public static GitHubRepoId Parse(string text)
        {
            if (TryParse(text, out var result))
                return result;

            throw new FormatException();
        }

        public static bool TryParse(string text, out GitHubRepoId result)
        {
            result = default;

            var parts = text.Split("/");
            if (parts.Length != 2)
                return false;

            var owner = parts[0].Trim();
            var repo = parts[1].Trim();

            if (owner.Length == 0 || repo.Length == 0)
                return false;

            result = new GitHubRepoId(owner, repo);
            return true;
        }

        public int CompareTo(object obj)
        {
            if (obj is GitHubRepoId other)
                return CompareTo(other);
            return 1;
        }

        public int CompareTo([AllowNull] GitHubRepoId other)
        {
            var result = string.Compare(Owner, other.Owner);
            if (result != 0)
                return result;

            return string.Compare(Name, other.Name);
        }

        public override bool Equals(object obj)
        {
            return obj is GitHubRepoId id && Equals(id);
        }

        public bool Equals(GitHubRepoId other)
        {
            return Owner == other.Owner &&
                   Name == other.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Owner, Name);
        }

        public static bool operator ==(GitHubRepoId left, GitHubRepoId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GitHubRepoId left, GitHubRepoId right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return $"{Owner}/{Name}";
        }
    }
}
