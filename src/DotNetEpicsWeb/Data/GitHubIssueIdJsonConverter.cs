using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubIssueIdJsonConverter : JsonConverter<GitHubIssueId>
    {
        public override GitHubIssueId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            return GitHubIssueId.Parse(text);
        }

        public override void Write(Utf8JsonWriter writer, GitHubIssueId value, JsonSerializerOptions options)
        {
            var text = value.ToString();
            writer.WriteStringValue(text);
        }
    }
}
