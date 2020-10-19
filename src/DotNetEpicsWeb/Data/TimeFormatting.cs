using System;

namespace DotNetEpicsWeb.Data
{
    public static class TimeFormatting
    {
        public static string Format(TimeSpan elapsedTime)
        {
            var negated = elapsedTime.Ticks < 0;
            if (negated)
                elapsedTime = elapsedTime.Negate();

            var totalYears = Math.Round(elapsedTime.TotalDays / 365, 0, MidpointRounding.AwayFromZero);
            var totalDays = Math.Round(elapsedTime.TotalDays, 0, MidpointRounding.AwayFromZero);
            var totalHours = Math.Round(elapsedTime.TotalHours, 0, MidpointRounding.AwayFromZero);
            var totalMinutes = Math.Round(elapsedTime.TotalMinutes, 0, MidpointRounding.AwayFromZero);

            var suffix = negated ? "from now" : "ago";

            if (totalYears > 1)
                return $"{totalYears:N0} years {suffix}";
            else if (totalDays > 60)
                return $"{totalDays / 30:N0} months {suffix}";
            else if (totalDays > 1)
                return $"{totalDays:N0} days {suffix}";
            else if (totalHours > 1)
                return $"{totalHours:N0} hours {suffix}";
            else if (totalMinutes > 1)
                return $"{totalMinutes:N0} minutes {suffix}";
            else
                return $"just now";
        }

        public static string FormatRelative(this DateTimeOffset dateTimeOffset)
        {
            var elapased = DateTimeOffset.Now.Subtract(dateTimeOffset);
            return Format(elapased);
        }
    }

}
