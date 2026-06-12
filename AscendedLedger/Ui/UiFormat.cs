using System;
using System.Globalization;

namespace AscendedLedger.Ui;

/// <summary>
/// Human-readable formatting for ledger values, shared by the window views.
/// Lives at the UI edge so Core stays culture-agnostic; output follows the
/// player's current culture.
/// </summary>
internal static class UiFormat {
    /// <summary>Formats a gil amount with thousands separators.</summary>
    public static string Gil(long amount) => amount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Formats a unit or item count with thousands separators.</summary>
    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a duration as its two most significant units ("12d 4h",
    /// "4h 32m", "32m"), flooring the remainder; negative durations (clock
    /// skew) render as zero.
    /// </summary>
    public static string Age(TimeSpan duration) {
        if (duration < TimeSpan.Zero) {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1) {
            return $"{(long)duration.TotalDays}d {duration.Hours}h";
        }

        return duration.TotalHours >= 1 ? $"{duration.Hours}h {duration.Minutes}m" : $"{duration.Minutes}m";
    }
}
