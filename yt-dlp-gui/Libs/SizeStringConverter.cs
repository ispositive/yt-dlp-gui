using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Diagnostics; // For Debug.WriteLine

namespace yt_dlp_gui.Libs {
    public struct ParsedSizeInfo {
        public long? FilesizeInBytes { get; }
        public bool IsApproximate { get; }
        public bool Success { get; }

        public ParsedSizeInfo(long? size, bool approx, bool success) {
            FilesizeInBytes = size;
            IsApproximate = approx;
            Success = success;
        }
    }

    public static class SizeStringConverter {
        // Regex to extract the numeric value and the unit.
        // Group 1: Optional '~'
        // Group 2: Numeric value
        // Group 3: Unit (KiB, MiB, GiB, TiB, KB, MB, GB, TB, B, or empty)
        private static readonly Regex SizePatternRegex = new Regex(
            @"^\s*(~)?\s*([0-9\.]+)\s*([KMGT]?i?B|[KMGTB])?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public static ParsedSizeInfo TryParseHumanReadableSize(string sizeString) {
            if (string.IsNullOrWhiteSpace(sizeString)) {
                return new ParsedSizeInfo(null, false, false);
            }

            var match = SizePatternRegex.Match(sizeString);

            if (!match.Success) {
                // Debug.WriteLine($"SizeStringConverter: Could not parse size string: '{sizeString}'");
                return new ParsedSizeInfo(null, false, false);
            }

            bool isApproximate = match.Groups[1].Success; // Check if '~' was present
            string numberPart = match.Groups[2].Value;
            string unitPart = match.Groups[3].Success ? match.Groups[3].Value.ToUpperInvariant() : "B"; // Default to Bytes if no unit

            if (!double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double numericValue)) {
                // Debug.WriteLine($"SizeStringConverter: Could not parse numeric part: '{numberPart}' from '{sizeString}'");
                return new ParsedSizeInfo(null, isApproximate, false);
            }

            long multiplier = 1;
            switch (unitPart) {
                case "B":
                    multiplier = 1;
                    break;
                case "KIB":
                    multiplier = 1024L;
                    break;
                case "MIB":
                    multiplier = 1024L * 1024L;
                    break;
                case "GIB":
                    multiplier = 1024L * 1024L * 1024L;
                    break;
                case "TIB":
                    multiplier = 1024L * 1024L * 1024L * 1024L;
                    break;
                case "KB": // Standard definition (1000), though yt-dlp -F uses KiB for 1024.
                           // If only "KB" is seen, it might imply 1000 or 1024 depending on context.
                           // yt-dlp -F output explicitly uses "KiB", "MiB", etc. So this is a fallback.
                    multiplier = 1000L;
                    break;
                case "MB":
                    multiplier = 1000L * 1000L;
                    break;
                case "GB":
                    multiplier = 1000L * 1000L * 1000L;
                    break;
                case "TB":
                    multiplier = 1000L * 1000L * 1000L * 1000L;
                    break;
                default:
                    // Debug.WriteLine($"SizeStringConverter: Unknown unit: '{unitPart}' in '{sizeString}'");
                    return new ParsedSizeInfo(null, isApproximate, false); // Unknown unit
            }

            try {
                long filesizeInBytes = Convert.ToInt64(numericValue * multiplier);
                return new ParsedSizeInfo(filesizeInBytes, isApproximate, true);
            } catch (OverflowException) {
                // Debug.WriteLine($"SizeStringConverter: Overflow converting to bytes: '{sizeString}'");
                return new ParsedSizeInfo(null, isApproximate, false);
            }
        }
    }
}
