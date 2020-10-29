using System;
using System.Drawing;
using System.Text.Json.Serialization;

namespace DotNetEpicsWeb.Data
{
    public sealed class TreeNodeLabel
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

        private static int PerceivedBrightness(Color c)
        {
            return (int)Math.Sqrt(
                c.R * c.R * .241 +
                c.G * c.G * .691 +
                c.B * c.B * .068);
        }

        private static string GetForegroundColor(string backgroundColor)
        {
            var c = ColorTranslator.FromHtml($"#{backgroundColor}");
            var brightness = PerceivedBrightness(c);
            var foregroundColor = brightness > 130 ? "black" : "white";
            return foregroundColor;
        }
    }
}
