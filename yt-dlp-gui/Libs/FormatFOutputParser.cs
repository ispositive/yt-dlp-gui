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

            string[] lines = fOutput.Split(new[] { '\r\n', '\r', '\n' }, System.StringSplitOptions.None);

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

                    string filesizeStr = "";
                    if (effectiveFilesizeStartIndex >= 0 && effectiveFilesizeStartIndex < restOfLine.Length) {
                        if (effectiveFilesizeEndIndex > effectiveFilesizeStartIndex && effectiveFilesizeEndIndex <= restOfLine.Length) {
                            filesizeStr = restOfLine.Substring(effectiveFilesizeStartIndex, effectiveFilesizeEndIndex - effectiveFilesizeStartIndex).Trim();
                        } else {
                            // If end index is problematic, take from start index to end of restOfLine
                            filesizeStr = restOfLine.Substring(effectiveFilesizeStartIndex).Trim();
                        }
                    } else if (restOfLine.Contains("MiB") || restOfLine.Contains("KiB") || restOfLine.Contains("GiB") || restOfLine.Contains("TiB") || restOfLine.StartsWith("~")) {
                        // Fallback: if column calculation is off, try to find it by content if it looks like a size
                        // This is a weaker heuristic. Example: find a block with ~ or size units.
                        // This is complex to make robust. For now, we rely on column indices.
                        // If filesizeStr is empty here, it means it wasn't found by column logic.
                    }

                    // If filesizeStr is just "video" or "audio", it's likely a misparse from "video only" / "audio only"
                    if (filesizeStr.Equals("video", System.StringComparison.OrdinalIgnoreCase) ||
                        filesizeStr.Equals("audio", System.StringComparison.OrdinalIgnoreCase) ||
                        filesizeStr.Equals("only", System.StringComparison.OrdinalIgnoreCase)) {
                        filesizeStr = ""; // Reset if it seems to have captured part of "video only" or "audio only"
                    }

                    // If filesizeStr is a TBR value like "123k", it's not a filesize.
                    if (Regex.IsMatch(filesizeStr, @"^\d+k$")) {
                         // This might happen if columns are very shifted.
                         // Check if the *next* column seems to be filesize.
                         // This part is too complex for this iteration.
                         // For now, if it looks like TBR, assume it's not filesize.
                         // A truly robust parser would parse *all* columns by header positions.
                         // For now, if it's "Xk", clear it.
                         // A better check would be if the *next* field is a protocol or VCODEC.
                         // Let's assume if it's just digits followed by 'k', it's not a MiB/GiB filesize.
                         // However, TBR can be like "123.45k"
                         if (!filesizeStr.Contains(".")) { // Simple check: 130k vs 12.3MiB
                            // filesizeStr = ""; // Commenting this out, as "123k" could be a valid (though unlikely for video) filesize for yt-dlp -F
                         }
                    }


                    if (!string.IsNullOrWhiteSpace(formatId)) {
                         // Debug.WriteLine($"FormatFOutputParser: ID='{formatId}', Extracted FilesizeStr='{filesizeStr}' from line: '{line}'");
                        idToFilesizeMap[formatId] = filesizeStr;
                    }
                }
            }
            return idToFilesizeMap;
        }
    }
}
