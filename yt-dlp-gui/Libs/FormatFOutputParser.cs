using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics; // For Debug.WriteLine

namespace yt_dlp_gui.Libs {
    public static class FormatFOutputParser {
        // Regex to capture format ID, and the filesize string.
        // This regex assumes:
        // - Format ID is the first non-whitespace block.
        // - Filesize is in a column that typically contains numbers, units (KiB, MiB, GiB), and optionally a '~'.
        // - It tries to find the "FILESIZE" column header to locate the filesize data more reliably.
        // Groups:
        // 1: Format ID
        // 2: Filesize string (e.g., "~12.34MiB", "100.00KiB", or empty if no size)
        private static readonly Regex FormatLineRegex = new Regex(
            @"^\s*([a-zA-Z0-9\.\-_]+)\s+" + // Format ID (group 1)
            @"(?:[^\s]+\s+){0,3}" + // Extension, Resolution, FPS, HDR (variable number of columns, non-capturing)
            @"(?:(?:(?:[^\s]+)\s+){0,8})?" + // Up to 8 more potential columns before filesize (CH, PROTO, VCODEC, VBR, ACODEC, ABR, ASR, MORE INFO)
                                            // This part is tricky due to variability. We will refine with header parsing.
            @"(.+?)\s*$", // Placeholder for actual filesize string, to be replaced by dynamic regex
            RegexOptions.Compiled | RegexOptions.Multiline
        );

        // Simpler regex for lines that are clearly format lines (start with ID, EXT, RES)
        // Group 1: ID
        // Group 2: Extension
        // Group 3: Resolution / "audio"
        // Group 4: The rest of the line after these initial fields.
        private static readonly Regex InitialFormatLineRegex = new Regex(
            @"^\s*([a-zA-Z0-9\.\-_]+)\s+([a-zA-Z0-9]+)\s+([a-zA-Z0-9x]+(?:x[a-zA-Z0-9]+)?|audio(?: only)?)\s+(.*)$",
            RegexOptions.Compiled
        );


        public static Dictionary<string, string> Parse(string fOutput) {
            var idToFilesizeMap = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(fOutput)) {
                return idToFilesizeMap;
            }

            string[] lines = fOutput.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);

            int headerLineIndex = -1;
            int filesizeColumnStartIndex = -1;
            int filesizeColumnEndIndex = -1; // Exclusive

            // Find the header line and determine filesize column boundaries
            for (int i = 0; i < lines.Length; i++) {
                if (lines[i].Contains("FILESIZE") && lines[i].Contains("ID") && lines[i].Contains("EXT")) {
                    headerLineIndex = i;
                    // Try to find the start index of FILESIZE column
                    int filesizeHeaderPos = lines[i].IndexOf("FILESIZE");
                    if (filesizeHeaderPos != -1) {
                        // Filesize data starts roughly where its header starts.
                        // This is an approximation. A more robust way would be to count columns
                        // or use fixed-width assumptions if possible, but yt-dlp output is not strictly fixed-width.

                        // Find start of FILESIZE column data by looking at spaces in the line below header
                        // This assumes data lines align somewhat with headers.
                        string dataExampleLine = (i + 1 < lines.Length) ? lines[i+1] : "";

                        // Heuristic: Start of "FILESIZE" header text
                        filesizeColumnStartIndex = filesizeHeaderPos;

                        // Find end of "FILESIZE" column by looking for start of next header "TBR" or end of line
                        int tbrHeaderPos = lines[i].IndexOf("TBR");
                        if (tbrHeaderPos > filesizeHeaderPos) {
                            filesizeColumnEndIndex = tbrHeaderPos;
                        } else {
                            // If TBR is not found or before filesize, assume filesize goes to near end of that segment of headers
                            // Look for PROTO or VCODEC if TBR is not helpful
                             int protoHeaderPos = lines[i].IndexOf("PROTO");
                             if (protoHeaderPos > filesizeHeaderPos) filesizeColumnEndIndex = protoHeaderPos;
                             else {
                                int vcodecHeaderPos = lines[i].IndexOf("VCODEC");
                                if (vcodecHeaderPos > filesizeHeaderPos) filesizeColumnEndIndex = vcodecHeaderPos;
                                else filesizeColumnEndIndex = lines[i].Length; // Fallback: take up to end of line
                             }
                        }
                        // Trim the end index to ensure it's not capturing too much from next column
                        if (filesizeColumnEndIndex > filesizeColumnStartIndex) {
                           filesizeColumnEndIndex = lines[i].Substring(0, filesizeColumnEndIndex).TrimEnd().Length;
                        }

                    }
                    break;
                }
            }

            if (headerLineIndex == -1 || filesizeColumnStartIndex == -1) {
                Debug.WriteLine("FormatFOutputParser: Could not find header line or FILESIZE column. Parsing might be inaccurate.");
                // Fallback to a more general regex if headers aren't helping (less reliable)
                // This part is removed for now to focus on header-guided parsing.
                // If header parsing fails, we will return an empty map or rely on the user to report issues.
                return idToFilesizeMap; // Or throw an error, or use a very basic regex as a last resort.
            }

            for (int i = headerLineIndex + 1; i < lines.Length; i++) {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---") || line.Contains("RESOLUTION") || line.Contains("video only")) {
                    // Basic filter for non-data lines or known separators/repeated headers
                    // The "video only" check is a common part of the "MORE INFO" column, not a header itself
                    // but if a line *only* contains that, it might be a separator or artifact.
                    // A better check is if the line doesn't start with a typical ID pattern.
                    if (!Regex.IsMatch(line.TrimStart(), @"^[a-zA-Z0-9\.\-_]+")) continue;
                }

                var initialMatch = InitialFormatLineRegex.Match(line);
                if (initialMatch.Success) {
                    string formatId = initialMatch.Groups[1].Value.Trim();
                    string restOfLine = initialMatch.Groups[4].Value;

                    // Now, from restOfLine, try to extract the filesize based on filesizeColumnStartIndex
                    // This is relative to the start of the "restOfLine" string.
                    // We need to map filesizeColumnStartIndex (from full line) to an index in restOfLine.
                    // The header gives us: ID  EXT   RESOLUTION ... FILESIZE ...
                    // initialMatch.Groups[0] is the full matched line.
                    // initialMatch.Groups[1] is ID. initialMatch.Groups[2] is EXT. initialMatch.Groups[3] is RES.
                    // The length of these plus spaces is the offset.
                    int originalLinePrefixLength = line.IndexOf(restOfLine);

                    int effectiveFilesizeStartIndex = filesizeColumnStartIndex - originalLinePrefixLength;
                    int effectiveFilesizeEndIndex = filesizeColumnEndIndex - originalLinePrefixLength;

                    string filesizeStr = ""; // Default to empty

                    // Ensure indices are valid for restOfLine
                    int searchAreaStart = Math.Max(0, effectiveFilesizeStartIndex - 3); // Look back a few chars
                    searchAreaStart = Math.Min(searchAreaStart, restOfLine.Length);     // Cannot exceed length

                    int searchAreaEnd = Math.Min(restOfLine.Length, effectiveFilesizeEndIndex + 5); // Look forward a bit for end, cannot exceed length
                                                                                                // The +5 is generous to find end of unit like "MiB"
                                                                                                // if effectiveFilesizeEndIndex was too tight.

                    if (searchAreaStart < searchAreaEnd) {
                        int actualStartOfSize = -1;
                        for (int k = searchAreaStart; k < searchAreaEnd; k++) {
                            if (char.IsDigit(restOfLine[k]) || restOfLine[k] == '~') {
                                actualStartOfSize = k;
                                break;
                            }
                        }

                        if (actualStartOfSize != -1) {
                            // Found the start. Now determine the end.
                            // The end is likely before the next column's data starts, or before too many spaces.
                            // We can use effectiveFilesizeEndIndex as a primary guide for where the column *should* end.
                            // Allow some characters beyond it for the unit (e.g., "MiB").
                            int potentialEnd = Math.Min(restOfLine.Length, actualStartOfSize + 15); // Max 15 chars for a typical size string like "~1234.56MiB"

                            string extractedSubstring = restOfLine.Substring(actualStartOfSize, potentialEnd - actualStartOfSize);

                            // Use regex to match the actual filesize pattern from the extracted substring
                            // This helps trim any trailing garbage if potentialEnd was too generous.
                            // Regex: optional ~, digits and dot, optional unit.
                            var sizeMatch = System.Text.RegularExpressions.Regex.Match(extractedSubstring, @"^~?[0-9\.]+([KMGT]i?B|[B])?");
                            if (sizeMatch.Success) {
                                filesizeStr = sizeMatch.Value;
                            } else {
                                // If regex fails, maybe it's just a number (bytes without unit) or just tilde? Unlikely.
                                // Fallback to a simple trim if regex doesn't match, but this is less robust.
                                // For now, if primary regex fails, we might get an empty or partial string.
                                // This part might need more refinement if simple trimming is not enough.
                                // The initial problem was missing leading digit, which finding actualStartOfSize should fix.
                                // The goal is to pass a clean string like "~712.06MiB" or "712.06MiB" to SizeStringConverter.
                                // A simpler extraction if regex seems too complex for the subtask worker:
                                // Take substring from actualStartOfSize up to a reasonable length for a filesize string (e.g. 12-15 chars)
                                // and then Trim(). SizeStringConverter will do the final validation.
                                // Let's use the regex match for precision.
                            }
                        }
                    }

                    // The rest of the filtering logic (e.g. for "video", "audio", "Xk" if it was TBR) should be re-evaluated.
                    // For now, the primary goal is to fix the leading digit.
                    // The lines checking if filesizeStr is "video" or "audio" might still be useful.
                    // The check for "Xk" should be specific that it's NOT KiB, MiB etc.
                    // The SizeStringConverter will handle "123k" as "123KB" if no "i" is present.
                    // This might be okay if yt-dlp -F *never* lists actual filesizes in just "k" without "iB".
                    // The -F output shows "130k" for ABR/TBR, and "2.90MiB" for filesizes. So, "k" alone is likely TBR.

                    if (filesizeStr.EndsWith("k", StringComparison.OrdinalIgnoreCase) &&
                        !filesizeStr.EndsWith("KiB", StringComparison.OrdinalIgnoreCase) && // Ensure it's not "KiB" mistaken for "k"
                        !filesizeStr.EndsWith("KB", StringComparison.OrdinalIgnoreCase) ) { // Ensure it's not "KB"
                        // If it's just "123k", it's likely TBR/ABR, not a filesize in MiB/KiB etc.
                        // However, SizeStringConverter might interpret "123k" as 123 * 1000 bytes.
                        // The -F output distinguishes TBR (e.g. 130k) from FILESIZE (e.g. 3.02MiB).
                        // If our column parsing is correct, we should get the FILESIZE column's value.
                        // This check is a safeguard against gross column mis-alignment.
                        // For now, let's assume if we get something like "123k" here, it's a misparse from TBR column.
                        // This should be rare if filesizeColumnStartIndex/EndIndex are good.
                        // For safety, if it matches a typical TBR/ABR format and not a filesize unit, clear it.
                        if (System.Text.RegularExpressions.Regex.IsMatch(filesizeStr, @"^\d+(\.\d+)?k$")) {
                             // filesizeStr = ""; // Clear if it looks like a bitrate.
                             // Decided to keep this commented: if the column logic is right, it should be the filesize.
                             // SizeStringConverter will handle it. If it's "120k", it means 120KB.
                        }
                    }

                    // The existing filtering for "video" or "audio" in filesizeStr can remain:
                    if (filesizeStr.Equals("video", System.StringComparison.OrdinalIgnoreCase) ||
                        filesizeStr.Equals("audio", System.StringComparison.OrdinalIgnoreCase) ||
                        filesizeStr.Equals("only", System.StringComparison.OrdinalIgnoreCase)) {
                        filesizeStr = "";
                    }

                    // Assign to map
                    if (!string.IsNullOrWhiteSpace(formatId)) {
                        idToFilesizeMap[formatId] = filesizeStr;
                    }
                }
            }
            return idToFilesizeMap;
        }
    }
}
