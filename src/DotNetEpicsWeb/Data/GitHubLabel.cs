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

        private static Color ParseColor(string color)
        {
            if (!string.IsNullOrEmpty(color) && color.Length == 6 &&
                int.TryParse(color.Substring(0, 2), NumberStyles.HexNumber, null, out var r) &&
                int.TryParse(color.Substring(2, 2), NumberStyles.HexNumber, null, out var g) &&
                int.TryParse(color.Substring(4, 2), NumberStyles.HexNumber, null, out var b))
            {
                return Color.FromArgb(r, g, b);
            }

            return Color.Black;
        }

        private static int PerceivedBrightness(string color)
        {
            var c = ParseColor(color);
            return (int)Math.Sqrt(
                c.R * c.R * .241 +
                c.G * c.G * .691 +
                c.B * c.B * .068);
        }

        private static string GetForegroundColor(string backgroundColor)
        {
            var brightness = PerceivedBrightness(backgroundColor);
            var foregroundColor = brightness > 130 ? "black" : "white";
            return foregroundColor;
        }
    }
}
