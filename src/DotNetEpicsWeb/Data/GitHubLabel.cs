using System;
using System.Drawing;
using System.Globalization;
using System.Text.Json.Serialization;

namespace DotNetEpicsWeb.Data
{
    public sealed class GitHubLabel
    {
        private string _foregroundColor;

        public string Name { get; set; }
        public string BackgroundColor { get; set; }

        [JsonIgnore]
        public string ForegroundColor
        {
            get
            {
                if (_foregroundColor == null)
                    _foregroundColor = GetForegroundColor(BackgroundColor);

                return _foregroundColor;
            }
        }

        private static string GetForegroundColor(string backgroundColor)
        {
            // turn the background color into Color obj
            var c = ColorTranslator.FromHtml($"#{backgroundColor}");

            // calculate best foreground color
            return (((c.R + c.B + c.G) / 3) > 128) ? "black" : "white";
        }
    }
}
