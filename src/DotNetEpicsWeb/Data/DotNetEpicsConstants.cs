using System.Collections.Generic;

namespace DotNetEpicsWeb.Data
{
    public static class DotNetEpicsConstants
    {
        public static string ApplicationName => "dotnet-epics";

        public const string LabelEpic = "Epic";
        public const string LabelExperience = "Experience";
        public const string LabelUserStory = "User Story";
        public const string LabelIssue = "Issue";

        public static IReadOnlyList<string> Labels => new[]
        {
            LabelEpic,
            LabelExperience,
            LabelUserStory
        };
    }
}
