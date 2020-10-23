using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DotNetEpicsWeb.Data
{
    public sealed class FilterString : IReadOnlyList<FilterStringToken>, IEquatable<FilterString>
    {
        private readonly FilterStringToken[] _tokens;

        public static FilterString Empty { get; } = new FilterString(Enumerable.Empty<FilterStringToken>());

        private FilterString(IEnumerable<FilterStringToken> tokens)
        {
            _tokens = tokens.ToArray();
        }

        public int Count => _tokens.Length;

        public FilterStringToken this[int index] => _tokens[index];

        public IEnumerator<FilterStringToken> GetEnumerator()
        {
            return _tokens.AsEnumerable().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public string GetValue(string key)
        {
            var result = (string)null;

            foreach (var token in _tokens)
                if (string.Equals(token.Key, key, StringComparison.OrdinalIgnoreCase))
                    result = token.Value;

            return result;
        }

        public string[] GetValues()
        {
            return _tokens.Where(t => t.Key == null && t.Value.Length > 0)
                          .Select(t => t.Value)
                          .ToArray();
        }

        public string[] GetValues(string key)
        {
            return _tokens.Where(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase) && t.Value.Length > 0)
                          .Select(t => t.Value)
                          .ToArray();
        }

        public FilterString AddValue(string key, string value)
        {
            if (GetValue(key) == value)
                return this;

            var tokens = _tokens.ToList();
            EnsureLastIsSeparator(tokens);
            tokens.Add(CreateToken(key, value));
            return new FilterString(tokens);
        }

        public FilterString SetValue(string key, string value)
        {
            var newTokens = (List<FilterStringToken>)null;
            var keyValueText = (string)null;

            for (var i = 0; i < _tokens.Length; i++)
            {
                var token = _tokens[i];
                if (string.Equals(token.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (newTokens == null)
                    {
                        newTokens = new List<FilterStringToken>(_tokens);
                        newTokens.RemoveRange(i, newTokens.Count - i);
                    }

                    if (value != null && keyValueText == null)
                    {
                        var newToken = CreateToken(key, value);
                        newTokens.Add(newToken);
                    }
                }
                else if (newTokens != null)
                {
                    newTokens.Add(token);
                }
            }

            if (newTokens == null)
                return AddValue(key, value);

            return new FilterString(newTokens);
        }

        public FilterString SetValues(string key, IReadOnlyCollection<string> values)
        {
            if (values.Count == 0)
                return SetValue(key, null);
            else if (values.Count == 1)
                return SetValue(key, values.FirstOrDefault());

            var newTokens = (List<FilterStringToken>)null;

            foreach (var value in values)
            {
                if (newTokens == null)
                    newTokens = SetValue(key, values.FirstOrDefault()).ToList();

                EnsureLastIsSeparator(newTokens);
                var newToken = CreateToken(key, value);
                newTokens.Add(newToken);
            }

            return new FilterString(newTokens);
        }

        public FilterString SetValues(IReadOnlyCollection<string> values)
        {
            if (values.Count == 0)
                return ClearValues();

            var newTokens = (List<FilterStringToken>)null;

            foreach (var value in values)
            {
                if (newTokens == null)
                    newTokens = ClearValues().ToList();

                EnsureLastIsSeparator(newTokens);
                var newToken = CreateToken(value);
                newTokens.Add(newToken);
            }

            return new FilterString(newTokens);
        }

        public FilterString ClearKeys()
        {
            var tokens = _tokens.Where(t => string.IsNullOrEmpty(t.Key));
            var filteredTokens = new List<FilterStringToken>();
            var needsSeparator = false;

            foreach (var token in tokens)
            {
                var isSeparator = string.IsNullOrWhiteSpace(token.RawText);
                if (isSeparator && !needsSeparator)
                    continue;

                filteredTokens.Add(token);
                needsSeparator = true;
            }

            // Now remove all whitespace from the end

            for (var i = filteredTokens.Count - 1; i >= 0; i--)
            {
                var isSeparator = string.IsNullOrWhiteSpace(filteredTokens[i].RawText);
                if (isSeparator)
                    filteredTokens.RemoveAt(i);
                else
                    break;
            }

            return new FilterString(filteredTokens);
        }

        public FilterString ClearValues()
        {
            var tokens = _tokens.Where(t => !string.IsNullOrEmpty(t.Key));
            var filteredTokens = new List<FilterStringToken>();
            var needsSeparator = false;

            foreach (var token in tokens)
            {
                var isSeparator = string.IsNullOrWhiteSpace(token.RawText);
                if (isSeparator && !needsSeparator)
                    continue;

                filteredTokens.Add(token);
                needsSeparator = true;
            }

            return new FilterString(filteredTokens);
        }

        public static FilterString Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Empty;

            var tokens = new List<FilterStringToken>();

            var matches = Regex.Matches(text, "(?<text>(?<key>[a-z]+):(?<value>\"[^\"]+\"|\\S+))|(?<text>\"[^\"]+\")|(?<text>\\S+)|(?<text>\\s+)");
            foreach (Match match in matches)
            {
                var rawText = match.Groups["text"].Value;
                var key = match.Groups["key"].Value;
                var value = Unescape(match.Groups["value"].Value);

                if (string.IsNullOrEmpty(key))
                    value = Unescape(rawText);

                var token = new FilterStringToken(rawText, key, value);
                tokens.Add(token);
            }

            return new FilterString(tokens);
        }

        private static FilterStringToken CreateToken(string value)
        {
            var escapedValue = Escape(value);
            var rawText = escapedValue;
            var newToken = new FilterStringToken(rawText, string.Empty, value);
            return newToken;
        }

        private static FilterStringToken CreateToken(string key, string value)
        {
            var escapedValue = Escape(value);
            var rawText = $"{key}:{escapedValue}";
            var newToken = new FilterStringToken(rawText, key, value);
            return newToken;
        }

        private static FilterStringToken CreateSeparator()
        {
            return CreateToken(" ");
        }

        private static string Escape(string value)
        {
            var escapedValue = value;
            if (!string.IsNullOrWhiteSpace(value) && value.Any(c => char.IsWhiteSpace(c)))
                escapedValue = "\"" + value + "\"";
            return escapedValue;
        }

        private static string Unescape(string value)
        {
            if (!string.IsNullOrEmpty(value) && value.Length > 1 && value.StartsWith("\"") && value.EndsWith("\""))
                return value[1..^1];

            return value;
        }

        private static void EnsureLastIsSeparator(List<FilterStringToken> tokens)
        {
            if (tokens.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(tokens.Last().RawText))
                return;

            tokens.Add(CreateSeparator());
        }

        public override string ToString()
        {
            return string.Concat(_tokens.Select(t => t.RawText));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FilterString);
        }

        public bool Equals(FilterString other)
        {
            return other != null &&
                   Count == other.Count &&
                   _tokens.Zip(other, (t, o) => EqualityComparer<FilterStringToken>.Default.Equals(t, o)).All(r => r);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_tokens, Count);
        }

        public static bool operator ==(FilterString left, FilterString right)
        {
            return EqualityComparer<FilterString>.Default.Equals(left, right);
        }

        public static bool operator !=(FilterString left, FilterString right)
        {
            return !(left == right);
        }
    }
}
