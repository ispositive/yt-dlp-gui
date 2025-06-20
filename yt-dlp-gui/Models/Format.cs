﻿using Swordfish.NET.Collections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using Newtonsoft.Json; // Added for JsonConverter attribute

namespace yt_dlp_gui.Models {
    public enum FormatType { video, audio, package, other }
    [JsonConverter(typeof(FormatFilesizeConverter))] // Added JsonConverter attribute
    public class Format : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public decimal? asr { get; set; } = null;

        private long? _filesize = null;
        public long? filesize {
            get => _filesize;
            set {
                if (_filesize != value) {
                    _filesize = value;
                    OnPropertyChanged(nameof(filesize));
                    OnPropertyChanged(nameof(FilesizeDisplayString));
                }
            }
        }

        private bool _isFilesizeApprox = false;
        public bool isFilesizeApprox {
            get => _isFilesizeApprox;
            set {
                if (_isFilesizeApprox != value) {
                    _isFilesizeApprox = value;
                    OnPropertyChanged(nameof(isFilesizeApprox));
                    OnPropertyChanged(nameof(FilesizeDisplayString));
                }
            }
        }
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

        private string ConvertBytesToHumanReadable(long bytes) {
            if (bytes == 0) return "0 B";
            long k = 1024;
            string[] sizes = { "B", "KiB", "MiB", "GiB", "TiB" };
            int i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
            double size = bytes / Math.Pow(k, i);
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}{1}", size, sizes[i]);
        }

        public string FilesizeDisplayString {
            get {
                if (filesize.HasValue) {
                    string sizeStr = ConvertBytesToHumanReadable(filesize.Value);
                    return isFilesizeApprox ? $"~{sizeStr}" : sizeStr;
                }
                return "N/A";
            }
        }
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
                // Filesize processing is now handled by FormatFilesizeConverter.
                // The old logic for determining filesize and isFilesizeApprox is removed.

                // All other processing of 'row' (type classification, codec renaming, etc.) follows this block.
                // No other part of this method should alter 'row.isFilesizeApprox'.

                // ----- Start of code to restore -----
                bool vcodecPresent = !string.IsNullOrEmpty(row.vcodec) && row.vcodec.ToLowerInvariant() != "none";
                bool acodecPresent = !string.IsNullOrEmpty(row.acodec) && row.acodec.ToLowerInvariant() != "none";

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
                    row.type = FormatType.audio;
                } else {
                    // Fallback logic
                    bool isLikelyAudio = !string.IsNullOrEmpty(row.format) && row.format.ToLowerInvariant().Contains("audio only");
                    bool isLikelyVideo = row.height.HasValue && row.height.Value > 0 &&
                                         (!string.IsNullOrEmpty(row.format) && !row.format.ToLowerInvariant().Contains("storyboard") && !row.format.ToLowerInvariant().Contains("images"));

                    if (isLikelyAudio) {
                        row.type = FormatType.audio;
                    } else if (isLikelyVideo) {
                        row.type = FormatType.video;
                        // Ensure resolution is set if we reclassify as video here
                        if (row.height.HasValue && row.width.HasValue) {
                            row.resolution = $"{row.width.Value}x{row.height.Value}";
                        } else if (!string.IsNullOrEmpty(row.format)) {
                            // Attempt to parse WxH from format string if vcodec was missing and resolution not set
                            // Ensure Regex is available: using System.Text.RegularExpressions;
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
                // ----- End of code to restore -----
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
