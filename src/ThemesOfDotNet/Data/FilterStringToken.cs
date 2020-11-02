using System;
using System.Collections.Generic;

namespace ThemesOfDotNet.Data
{
    public sealed class FilterStringToken : IEquatable<FilterStringToken>
    {
        public FilterStringToken(string rawText, string key, string value)
        {
            RawText = rawText;
            Key = key;
            Value = value;
        }

        public string RawText { get; }
        public string Key { get; }
        public string Value { get; }

        public override bool Equals(object obj)
        {
            return Equals(obj as FilterStringToken);
        }

        public bool Equals(FilterStringToken other)
        {
            return other != null &&
                   RawText == other.RawText &&
                   Key == other.Key &&
                   Value == other.Value;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RawText, Key, Value);
        }

        public static bool operator ==(FilterStringToken left, FilterStringToken right)
        {
            return EqualityComparer<FilterStringToken>.Default.Equals(left, right);
        }

        public static bool operator !=(FilterStringToken left, FilterStringToken right)
        {
            return !(left == right);
        }
    }
}
