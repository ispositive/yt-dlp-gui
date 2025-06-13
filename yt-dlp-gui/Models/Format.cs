using Swordfish.NET.Collections;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace yt_dlp_gui.Models {
    public enum FormatType { video, audio, package, other }
    public class Format : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        public decimal? asr { get; set; } = null;
        public long? filesize { get; set; } = null; //bytes
        public long? filesize_approx { get; set; } = null;
        public bool isFilesizeApprox { get; set; } = false;
        public string format_id { get; set; } = string.Empty;
        public string format_note { get; set; } = "";
        public decimal? fps { get; set; } = null;
        public decimal? height { get; set; } = null;
        public decimal? width { get; set; } = null;
        public string ext { get; set; } = string.Empty;
        public string vcodec { get; set; } = string.Empty;
        public string acodec { get; set; } = string.Empty;
        public string? dynamic_range { get; set; } = null;
        public decimal? tbr { get; set; } = null; //k
        public decimal? vbr { get; set; } = null; //k
        public decimal? abr { get; set; } = null; //k
        public decimal? preference { get; set; } = null;
        public string container { get; set; } = string.Empty;
        public string audio_ext { get; set; } = "none";
        public string video_ext { get; set; } = "none";
        public FormatType type { get; set; } = FormatType.other;
        public string format { get; set; } = string.Empty;
        public string resolution { get; set; } = string.Empty;
        public string info { get; set; } = string.Empty;
    }
    public class ComparerAudio : IComparer<Format> {
        public int Compare(Format? x, Format? y) {
            if (x == null && y == null) return 0;
            //var r = 0;
            //比较 ABR
            if (x.abr.HasValue && y.abr.HasValue) {
                var max = Math.Max(x.abr.Value, y.abr.Value);
                var min = Math.Min(x.abr.Value, y.abr.Value);
                if (max != 0m) {
                    var e = 1m - min / max;
                    if (e > 0.1m) {
                        return x.abr.Value > y.abr.Value ? -1 : 1;
                    }
                }
            }
            //比较 ASR
            if (x.asr.HasValue && y.asr.HasValue) {
                return x.asr.Value > y.asr.Value ? -1 : 1;
            }

            return 0;
        }
        public static ComparerAudio Comparer = new ComparerAudio();
    }
    public class ComparerVideo : IComparer<Format> {
        public int Compare(Format? x, Format? y) {
            if (x == null && y == null) return 0;
            //var r = 0;
            //比较 resolution
            if (x.height.HasValue && y.height.HasValue) {
                var xr = (x.width ?? 1) * x.height.Value;
                var yr = (y.width ?? 1) * y.height.Value;
                if (xr != yr) {
                    return xr > yr ? -1 : 1;
                }
            }
            //比较 vbr
            if (x.vbr.HasValue && y.vbr.HasValue) {
                var max = Math.Max(x.vbr.Value, y.vbr.Value);
                var min = Math.Min(x.vbr.Value, y.vbr.Value);
                if (max != 0m) {
                    var e = 1m - min / max;
                    if (e > 0.1m) {
                        return x.vbr.Value > y.vbr.Value ? -1 : 1;
                    }
                }
            }
            //比较 格式
            var prefer = new List<string>() { "VP9", "AV1", "H.264" };
            var xf = prefer.IndexOf(x.vcodec);
            var yf = prefer.IndexOf(y.vcodec);
            if (xf != yf) return xf > yf ? -1 : 1;

            return 0;
        }
        public static ComparerVideo Comparer = new ComparerVideo();
    }
    public static class ExtensionFormat {
        public static void LoadFromVideo(this ConcurrentObservableCollection<Format> source, List<Format> from) { //, Video from
            foreach (var row in from) { //from.formats
                // Ensure 'row' is a 'Format' object.
                // The 'filesize' and 'filesize_approx' properties on 'row' are assumed to be populated
                // directly by JSON deserialization by this point if the fields existed in JSON.

                long? original_filesize_json = row.filesize; // Value from JSON "filesize" field
                long? original_filesize_approx_json = row.filesize_approx; // Value from JSON "filesize_approx" field

                if (original_filesize_approx_json.HasValue) {
                    // yt-dlp provided an approximate filesize. This is the one to use for display.
                    row.filesize = original_filesize_approx_json.Value;
                    row.isFilesizeApprox = true;
                } else if (original_filesize_json.HasValue) {
                    // No approximate filesize from yt-dlp, but an exact one is present.
                    // row.filesize already holds this value from JSON deserialization.
                    row.isFilesizeApprox = false;
                } else {
                    // Neither filesize_approx nor filesize were present in JSON for this format.
                    row.filesize = null; // Ensure filesize is explicitly null if no size info at all.
                    row.isFilesizeApprox = false;
                }

                // Classification logic
                bool vcodecPresent = !string.IsNullOrEmpty(row.vcodec) && row.vcodec.ToLower() != "none";
                bool acodecPresent = !string.IsNullOrEmpty(row.acodec) && row.acodec.ToLower() != "none";

                if (vcodecPresent && acodecPresent) {
                    row.type = FormatType.package;
                    if (row.height.HasValue && row.width.HasValue) {
                        row.resolution = $"{row.width.Value}x{row.height.Value}";
                    }
                } else if (vcodecPresent) {
                    row.type = FormatType.video;
                    if (row.height.HasValue && row.width.HasValue) {
                        row.resolution = $"{row.width.Value}x{row.height.Value}";
                    }
                } else if (acodecPresent) {
                    // acodec is present, vcodec is not. This includes acodec == "unknown".
                    row.type = FormatType.audio;
                } else {
                    // Both vcodec and acodec are effectively absent (null, empty, or "none").
                    // This is where we need to be careful for misclassified videos or audios.
                    bool isLikelyAudio = row.format != null && row.format.ToLower().Contains("audio only");
                    bool isLikelyVideo = row.height.HasValue && row.height.Value > 0 &&
                                         (row.format != null && !row.format.ToLower().Contains("storyboard") && !row.format.ToLower().Contains("images"));

                    if (isLikelyAudio) {
                        row.type = FormatType.audio;
                    } else if (isLikelyVideo) {
                        row.type = FormatType.video;
                        // Ensure resolution is set if we reclassify as video here
                        if (row.height.HasValue && row.width.HasValue) {
                            row.resolution = $"{row.width.Value}x{row.height.Value}";
                        } else if (row.format != null) {
                            // Attempt to parse WxH from format string if vcodec was missing and resolution not set
                            var match = System.Text.RegularExpressions.Regex.Match(row.format, @"(\d{3,})x(\d{3,})");
                            if (match.Success && decimal.TryParse(match.Groups[1].Value, out decimal parsedWidth) && decimal.TryParse(match.Groups[2].Value, out decimal parsedHeight)) {
                                row.width = parsedWidth;
                                row.height = parsedHeight;
                                row.resolution = $"{row.width.Value}x{row.height.Value}";
                            }
                        }
                    } else {
                        row.type = FormatType.other;
                    }
                }
                //Video Codec
                if (row.vcodec.StartsWith("vp9", StringComparison.InvariantCultureIgnoreCase)) {
                    row.vcodec = "VP9";
                } else if (row.vcodec.StartsWith("av01", StringComparison.InvariantCultureIgnoreCase)) {
                    row.vcodec = "AV1";
                } else if (row.vcodec.StartsWith("avc", StringComparison.InvariantCultureIgnoreCase)) {
                    row.vcodec = "H.264";
                }
                //Audio Codec
                if (row.acodec.StartsWith("mp4a", StringComparison.InvariantCultureIgnoreCase)) {
                    row.acodec = "AAC";
                } else if (row.acodec.StartsWith("opus", StringComparison.InvariantCultureIgnoreCase)) {
                    row.acodec = "OPUS";
                }
            }
            source.Reset(from); //from.formats
        }
    }
}
