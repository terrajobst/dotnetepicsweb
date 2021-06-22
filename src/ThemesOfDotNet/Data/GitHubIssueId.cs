using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ThemesOfDotNet.Data
{
    public struct GitHubIssueId : IEquatable<GitHubIssueId>, IComparable, IComparable<GitHubIssueId>
    {
        public GitHubIssueId(string owner, string repo, int number)
        {
            Owner = owner;
            Repo = repo;
            Number = number;
        }

        public string Owner { get; }
        public string Repo { get; }
        public int Number { get; }

        public static GitHubIssueId Parse(string text)
        {
            if (TryParse(text, out var result))
                return result;

            throw new FormatException();
        }

        public static bool TryParse(string text, out GitHubIssueId result)
        {
            result = default;

            if (string.IsNullOrEmpty(text))
                return false;

            var match = Regex.Match(text, @"
                https?://github.com/(?<owner>[a-zA-Z0-9-]+)/(?<repo>[a-zA-Z0-9-]+)/issues/(?<number>[0-9]+)|
                https?://api.github.com/repos/(?<owner>[a-zA-Z0-9-]+)/(?<repo>[a-zA-Z0-9-]+)/issues/(?<number>[0-9]+)|
                (?<owner>[a-zA-Z0-9-]+)/(?<repo>[a-zA-Z0-9-]+)\#(?<number>[0-9]+)", RegexOptions.IgnorePatternWhitespace);
            if (!match.Success)
                return false;

            var owner = match.Groups["owner"].Value;
            var repo = match.Groups["repo"].Value;
            var numberText = match.Groups["number"].Value;

            if (!int.TryParse(numberText, out var number))
                return false;

            result = new GitHubIssueId(owner, repo, number);
            return true;
        }

        public int CompareTo(object obj)
        {
            if (obj is GitHubIssueId other)
                return CompareTo(other);
            return 1;
        }

        public int CompareTo([AllowNull] GitHubIssueId other)
        {
            var result = string.Compare(Owner, other.Owner);
            if (result != 0)
                return result;

            result = string.Compare(Repo, other.Repo);
            if (result != 0)
                return result;

            return Number.CompareTo(other.Number);
        }

        public override bool Equals(object obj)
        {
            return obj is GitHubIssueId id && Equals(id);
        }

        public bool Equals(GitHubIssueId other)
        {
            return string.Equals(Owner, other.Owner, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Repo, other.Repo, StringComparison.OrdinalIgnoreCase) &&
                   Number == other.Number;
        }

        public override int GetHashCode()
        {
            var code = new HashCode();
            code.Add(Owner, StringComparer.OrdinalIgnoreCase);
            code.Add(Repo, StringComparer.OrdinalIgnoreCase);
            code.Add(Number);
            return code.ToHashCode();
        }

        public static bool operator ==(GitHubIssueId left, GitHubIssueId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GitHubIssueId left, GitHubIssueId right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return $"{Owner}/{Repo}#{Number}";
        }
    }
}
