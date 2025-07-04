using Libs;
using Libs.Yaml;
using Microsoft.Toolkit.Uwp.Notifications;
using Newtonsoft.Json;
using Swordfish.NET.Collections.Auxiliary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shell;
using WK.Libraries.SharpClipboardNS;
using yt_dlp_gui.Controls;
using yt_dlp_gui.Models;
using yt_dlp_gui.Wrappers;
using yt_dlp_gui.Libs; // For FormatFOutputParser and SizeStringConverter


namespace yt_dlp_gui.Views {
    public partial class Main :Window {
        private readonly ViewData Data = new();
        private List<DLP> RunningDLP = new();
        public Main() {
            InitializeComponent();
            DataContext = Data;
            ToastNotificationManagerCompat.OnActivated += ToastNotificationManagerCompat_OnActivated;

            //Load Configs
            InitGUIConfig();

            Topmost = Data.AlwaysOnTop;
            if (Data.RememberWindowStatePosition) {
                Top = Data.Top;
                Left = Data.Left;
            }
            if (Data.RememberWindowStateSize) {
                Width = Data.Width;
                Height = Data.Height;
            } else {
                Width = 600 * (Data.Scale / 100d);
                Height = 380 * (Data.Scale / 100d);
            }

            //Configuration Checking (./configs/*.*)
            InitConfiguration();

            //ScanDeps (YT-DLP, FFMPEG...)
            ScanDepends();

            //if `Target` Not exist, default app location
            if (!Directory.Exists(Data.TargetPath)) {
                Data.TargetPath = App.AppPath;
            }
            //Default Temp Dir
            if (string.IsNullOrWhiteSpace(Data.PathTEMP) || !Directory.Exists(GetTempPath)) {
                Data.PathTEMP = "%YTDLPGUI_TARGET%";
            }

            //Monitor Clipboard
            InitClipboard();

            //run update check
            _ = Task.Run(Inits); // CS4014: Explicitly fire and forget for top-level init

            //Output Lang templet
            //Yaml.Save(App.Path(App.Folders.root, "lang.yaml"), new Lang());
        }

        private void ToastNotificationManagerCompat_OnActivated(ToastNotificationActivatedEventArgsCompat e) {
            var args = ToastArguments.Parse(e.Argument);
            if (args.Contains("action")) {
                switch (args["action"]) {
                    case "browse":
                        if (File.Exists(args["file"])) _ = Util.Explorer(args["file"]);
                        break;
                }
            }
        }

        private void ChangeScale(int present) {
            var scaleRatio = present / 100d;
            var grid = Template.FindName("MainGrid", this) as Grid; 
            if (grid != null) {
                var scaleTransform = new ScaleTransform(scaleRatio, scaleRatio);
                grid.LayoutTransform = scaleTransform;
                WindowChrome.SetWindowChrome(this, new() {
                    CaptionHeight = 22 * scaleRatio,
                    ResizeBorderThickness = new Thickness(6),
                    CornerRadius = new CornerRadius(0),
                    GlassFrameThickness = new Thickness(1),
                    NonClientFrameEdges = NonClientFrameEdges.None,
                    UseAeroCaptionButtons = false
                });
                grid.UpdateLayout();
            }
        }
        private string GetEnvPath(string path) {
            Dictionary<string, string> replacements = new() {
                {"%YTDLPGUI_TARGET%", Data.TargetPath},
                {"%YTDLPGUI_LOCALE%", App.AppPath}
            };
            foreach (KeyValuePair<string, string> pair in replacements) {
                string placeholder = pair.Key;
                string replacement = pair.Value;

                // Replace the placeholder with the replacement string
                path = path.Replace(placeholder, replacement);

                // Remove the part to the left of the replacement string
                int index = path.IndexOf(replacement);
                if (index >= 0) {
                    path = path.Substring(index);
                }

                // Remove duplicate directory separators
                path = path.Replace('/', '\\');
                while (path.Contains("\\\\")) {
                    path = path.Replace("\\\\", "\\");
                }
            }
            return Environment.ExpandEnvironmentVariables(path);
        }
        private string GetTempPath {
            get => GetEnvPath(Data.PathTEMP);
        }
        //Regex For Clipboard
        private Regex _frgPat = new Regex("<!--StartFragment-->(.*)<!--EndFragment-->", RegexOptions.Multiline | RegexOptions.Compiled);
        private Regex _matchUrls = new Regex(@"(https?|ftp|file)\://[A-Za-z0-9\.\-]+(/[A-Za-z0-9\?\&\=;\+\!'\\(\)\*\-\._~%]*)*", RegexOptions.Compiled);
        public void InitClipboard() {
            Data.PropertyChanged += (s, e) => {
                switch (e.PropertyName) {
                    case nameof(Data.ClipboardText):
                        var content = Data.ClipboardText;
                        var m = _matchUrls.Match(content);
                        if (m.Success) {
                            var capUrl = m.Value;
                            if (Util.UrlVaild(capUrl)) {
                                Data.Url = capUrl;
                                Analyze_Start();
                            }
                        }
                        break;
                    case nameof(Data.AlwaysOnTop):
                        Topmost = Data.AlwaysOnTop;
                        break;
                }
            };

            var sc = new SharpClipboard();
            sc.ClipboardChanged += (s, e) => {
                if (!Data.IsMonitor || Data.IsAnalyze || Data.IsDownload) return;
                if (e.ContentType == SharpClipboard.ContentTypes.Text) {
                    Data.ClipboardText = GetClipbaordText();
                }
            };
        }
        private string GetClipbaordText() {
            int maxTries = 10;
            int delayTime = 1000; // milliseconds
            int numTries = 0;
            while (numTries < maxTries) {
                try {
                    var content = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Html);
                    if (!string.IsNullOrWhiteSpace(content)) {
                        content = _frgPat.Match(content).Groups?[1].Value.Trim() ?? "";
                    } else {
                        content = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Text);
                    }
                    numTries = 0;
                    return content;
                } catch (Exception) {
                    numTries++;
                    Thread.Sleep(delayTime);
                }
            }
            return string.Empty;
        }
        public void InitGUIConfig() {
            Data.GUIConfig.Load(App.Path(App.Folders.root, App.AppName + ".yaml"));
            Util.PropertyCopy(Data.GUIConfig, Data);
            //Loaded and enabled auto save config
            Data.AutoSaveConfig = true;
        }
        public void InitConfiguration() {
            Data.Configs.Clear();
            Data.Configs.Add(new Config() { name = App.Lang.Main.ConfigurationNone });
            var cp = App.Path(App.Folders.configs);
            var fs = Directory.Exists(cp)
                ? Directory.EnumerateFiles(cp).OrderBy(x => x)
                : Enumerable.Empty<string>();
            fs.ForEach(x => {
                Data.Configs.Add(new Config() {
                    name = Path.GetFileNameWithoutExtension(x),
                    file = x
                });
            });
            Data.selectedConfig = Data.Configs.FirstOrDefault(x => x.file == Data.GUIConfig.ConfigurationFile, Data.Configs.First());
        }
        public void ScanDepends() {
            var isYoutubeDl = @"^youtube-dl\.exe";
            if (!string.IsNullOrWhiteSpace(Data.PathYTDLP) && File.Exists(Data.PathYTDLP)) {
                DLP.Path_DLP = Data.PathYTDLP;
            }
            if (!string.IsNullOrWhiteSpace(Data.PathAria2) && File.Exists(Data.PathAria2)) {
                DLP.Path_Aria2 = Data.PathAria2;
            }
            if (!string.IsNullOrWhiteSpace(Data.PathFFMPEG) && File.Exists(Data.PathFFMPEG)) {
                DLP.Path_FFMPEG = Data.PathFFMPEG;
                FFMPEG.Path_FFMPEG = Data.PathFFMPEG;
            }
            if (string.IsNullOrWhiteSpace(DLP.Path_DLP) ||
                string.IsNullOrWhiteSpace(DLP.Path_Aria2) ||
                string.IsNullOrWhiteSpace(FFMPEG.Path_FFMPEG)) {
                var deps = Directory.EnumerateFiles(App.AppPath, "*.exe", SearchOption.AllDirectories).ToList();
                deps = deps.Where(x => Path.GetFileName(App.AppExe) != Path.GetFileName(x)).ToList();
                var dep_ytdlp = deps.FirstOrDefault(x => Regex.IsMatch(Path.GetFileName(x), @"^(yt-dlp(_min|_x86|_x64)?|ytdl-patched.*?)\.exe"), "");
                var dep_ffmpeg = deps.FirstOrDefault(x => Regex.IsMatch(Path.GetFileName(x), @"^ffmpeg"), "");
                var dep_aria2 = deps.FirstOrDefault(x => Regex.IsMatch(Path.GetFileName(x), @"^aria2"), "");
                var dep_youtubedl = deps.FirstOrDefault(x => Regex.IsMatch(Path.GetFileName(x), isYoutubeDl), "");
                if (string.IsNullOrWhiteSpace(DLP.Path_DLP)) {
                    if (!string.IsNullOrWhiteSpace(dep_ytdlp)) {
                        Data.PathYTDLP = DLP.Path_DLP = dep_ytdlp;
                    } else if (!string.IsNullOrWhiteSpace(dep_youtubedl)) {
                        Data.PathYTDLP = DLP.Path_DLP = dep_youtubedl;
                    }
                }
                if (Regex.IsMatch(DLP.Path_DLP, isYoutubeDl)) DLP.Type = DLP.DLPType.youtube_dl;
                if (string.IsNullOrWhiteSpace(DLP.Path_Aria2)) {
                    Data.PathAria2 = DLP.Path_Aria2 = dep_aria2;
                }
                if (string.IsNullOrWhiteSpace(FFMPEG.Path_FFMPEG)) {
                    Data.PathFFMPEG = DLP.Path_FFMPEG = FFMPEG.Path_FFMPEG = dep_ffmpeg;
                }
            }
        }
        public async void Inits() {
            //check update
            var needcheck = false;
            var currentDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"); //"";

            if (!string.IsNullOrWhiteSpace(Data.LastVersion)) needcheck = true; //not yaml
            if (currentDate != Data.LastCheckUpdate) needcheck = true; //cross date

            if (needcheck) {
                var releaseData = await Web.GetLastTag(); // CS1998: Await the async call
                var last = releaseData?.FirstOrDefault(); // Add null conditional access
                if (last != null) {
                    Data.ReleaseData = releaseData;
                    Data.LastVersion = last.tag_name;
                    Data.LastCheckUpdate = currentDate;
                }
            }
            if (string.Compare(App.CurrentVersion, Data.LastVersion) < 0) {
                Data.NewVersion = true;
            }
        }
        private void Button_Analyze(object sender, RoutedEventArgs e) {
            Analyze_Start();
        }
        private void Analyze_Start() {
            Data.IsAnalyze = true;
            cc.SelectedIndex = -1;
            cv.SelectedIndex = -1;
            ca.SelectedIndex = -1;
            cs.SelectedIndex = -1;
            Data.Thumbnail = null;
            Data.Video = new();
            Data.NeedCookie = Data.UseCookie == UseCookie.Always;

            _ = Task.Run(() => { // CS4014: Explicitly fire and forget Analyze_Start's background work
                GetInfo();
                Data.IsAnalyze = false;

                if (Data.AutoDownloadAnalysed) {
                    //Download_Start();
                    if (Data.selectedVideo != null && Data.selectedAudio != null) {
                        Download_Start_Native();
                    }
                }
            });
        }
        private void GetInfo() {
            //Analyze
            var dlp = new DLP(Data.Url);
            if (Data.NeedCookie) dlp.Cookie(Data.CookieType);
            dlp.Proxy(Data.ProxyUrl, Data.ProxyEnabled);
            dlp.GetInfo();
            if (!string.IsNullOrWhiteSpace(Data.selectedConfig.file)) {
                dlp.LoadConfig(Data.selectedConfig.file);
            }
            if (Data.UseOutput) dlp.Output("%(title)s.%(ext)s"); //if not used config, default template
            ClearStatus();
            dlp.Exec(null, std => {
                //取得JSON
                Data.Video = JsonConvert.DeserializeObject<Video>(std, new JsonSerializerSettings() {
                    NullValueHandling = NullValueHandling.Ignore
                });

                // Save raw JSON
                if (Data.Video != null) {
                    string jsonDumpsPath = Path.Combine(App.AppPath, "JsonDumps");
                    Directory.CreateDirectory(jsonDumpsPath);
                    string videoTitle = Data.Video?.title ?? "UnknownVideo";
                    string invalidCharsRegex = string.Format("[{0}]", Regex.Escape(new string(Path.GetInvalidFileNameChars())));
                    string sanitizedTitle = Regex.Replace(videoTitle, invalidCharsRegex, "_");
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string jsonFilePath = Path.Combine(jsonDumpsPath, $"{sanitizedTitle}_{timestamp}.json");
                    try {
                        File.WriteAllText(jsonFilePath, std);
                    } catch (Exception ex) {
                        Debug.WriteLine($"Error saving JSON dump: {ex.Message}");
                    }
                }

                //Reading Chapters
                {
                    Data.Chapters.Clear();
                    if (Data.Video?.chapters?.Any() == true) {
                        Data.Chapters.Add(new Chapters() { title = App.Lang.Main.ChaptersAll, type = ChaptersType.None });
                        Data.Chapters.Add(new Chapters() { title = App.Lang.Main.ChaptersSplite, type = ChaptersType.Split });
                        Data.Chapters.AddRange(Data.Video.chapters);
                        Data.hasChapter = true;
                    } else {
                        Data.Chapters.Add(new Chapters() { title = App.Lang.Main.ChaptersNone, type = ChaptersType.None });
                        Data.hasChapter = false;
                    }
                    //Data.selectedChapter = Data.Chapters.First();
                }
                //读取 Formats 与 Thumbnails
                {
                    //Debug.WriteLine(JsonConvert.SerializeObject(Data.Video.chapters, Formatting.Indented));
                    Data.Formats.LoadFromVideo(Data.Video.formats);
                    Data.Thumbnails.Reset(Data.Video.thumbnails);
                    Data.RequestedFormats.LoadFromVideo(Data.Video.requested_formats);

                    // Augment format information using -F output
                    Debug.WriteLine("Attempting to fetch and parse -F output to augment format sizes.");
                    string fOutput = dlp.GetFormatListingOutput(); // Call the new method in DLP.cs

                    if (!string.IsNullOrWhiteSpace(fOutput)) {
                        Dictionary<string, string> filesizeMapF = FormatFOutputParser.Parse(fOutput); // Use the new parser

                        if (filesizeMapF.Any()) { // Check if map has any entries before iterating
                            Debug.WriteLine($"[AugmentLogic] Starting augmentation. Filesize map from -F has {filesizeMapF.Count} entries.");
                            foreach (var format in Data.Formats) {
                                string initialFilesizeDisplay = format.FilesizeDisplayString; // Get initial display value for logging
                                long? initialSizeBytes = format.filesize; // Get initial byte value

                                Debug.WriteLine($"[AugmentLogic] Processing Format ID: {format.format_id}. Initial Filesize (bytes): {initialSizeBytes?.ToString() ?? "null"}. Initial Display: '{initialFilesizeDisplay}'. IsApprox: {format.isFilesizeApprox}");

                                if (!format.filesize.HasValue) { // Key condition: Only augment if JSON didn't provide a size
                                    Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id} - JSON filesize is NULL. Attempting to use -F data.");
                                    if (filesizeMapF.TryGetValue(format.format_id, out string filesizeStrF) && !string.IsNullOrWhiteSpace(filesizeStrF)) {
                                        Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id} - Found raw filesize string from -F: '{filesizeStrF}'");

                                        ParsedSizeInfo parsedSize = SizeStringConverter.TryParseHumanReadableSize(filesizeStrF);
                                        if (parsedSize.Success && parsedSize.FilesizeInBytes.HasValue) {
                                            Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id} - Successfully parsed -F string. New Size (bytes): {parsedSize.FilesizeInBytes.Value}, IsApprox: {parsedSize.IsApproximate}");
                                            format.filesize = parsedSize.FilesizeInBytes.Value;
                                            format.isFilesizeApprox = parsedSize.IsApproximate;
                                        } else {
                                            Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id} - FAILED to parse -F string: '{filesizeStrF}'. Filesize remains NULL.");
                                        }
                                    } else {
                                        Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id} - No non-empty filesize string found in -F map. Filesize remains NULL.");
                                    }
                                } else {
                                    Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id} - JSON filesize HAS VALUE ({initialSizeBytes}). No augmentation from -F needed.");
                                }
                                // Log final state for this format after augmentation attempt
                                // Debug.WriteLine($"[AugmentLogic] Format ID: {format.format_id}. Final Filesize (bytes): {format.filesize?.ToString() ?? "null"}. Final Display: '{format.FilesizeDisplayString}'. Final IsApprox: {format.isFilesizeApprox}");
                            }
                        } else {
                            Debug.WriteLine("[AugmentLogic] Filesize map from -F is empty. No augmentation will be performed.");
                        }
                    } else {
                        Debug.WriteLine("[AugmentLogic] yt-dlp -F output was empty or could not be fetched.");
                    }
                }
                //读取 Subtitles
                {
                    var subs = Data.Video.subtitles.Select(x => {
                        var s = x.Value.FirstOrDefault(y => y.ext == "vtt");
                        if (s == null) return null;
                        s.key = x.Key;
                        return s;
                    }).Where(x => x != null).ToList();
                    Data.Subtitles.Clear();
                    if (subs.Any()) {
                        Data.Subtitles.Add(new Subs() { name = App.Lang.Main.SubtitleIgnore });
                        Data.hasSubtitle = true;
                    } else {
                        Data.Subtitles.Add(new Subs() { name = App.Lang.Main.SubtitleNone });
                        Data.hasSubtitle = false;
                    }
                    Data.Subtitles.AddRange(subs.OfType<Subs>().ToList());
                }
                var BestUrl = Data.Thumbnails.LastOrDefault()?.url;
                if (BestUrl != null && Web.Head(BestUrl)) {
                    Data.Thumbnail = BestUrl;
                } else {
                    Data.Thumbnail = Data.Video.thumbnail;
                }

                Data.SelectFormatBest(); //Make ComboBox Selected Item
                var full = string.Empty;
                if (Path.IsPathRooted(Data.Video._filename)) {
                    full = Path.GetFullPath(Data.Video._filename);
                } else {
                    full = Path.Combine(Data.TargetPath, Data.Video._filename);
                }
                //Data.TargetName = GetValidFileName(Data.Video.title) + ".tmp"; //预设档案名称
                Data.TargetName = full; //预设档案名称

            });
            dlp.Err(DLP.DLPError.Sign, () => {
                if (Data.UseCookie == UseCookie.WhenNeeded) {
                    Data.NeedCookie = true;
                    GetInfo();
                } else if (Data.UseCookie == UseCookie.Ask) {
                    var mb = System.Windows.Forms.MessageBox.Show(
                        $"{App.Lang.Dialog.CookieRequired}\n",
                        $"{App.AppName}",
                        MessageBoxButtons.YesNo);

                    if (mb == System.Windows.Forms.DialogResult.Yes) {
                        Data.NeedCookie = true;
                        GetInfo();
                    }
                }
            });
        }
        private void ClearStatus() {
            Data.DNStatus_Infos.Clear();
            Data.DNStatus_Video = new();
            Data.DNStatus_Audio = new();
            Data.VideoPersent = Data.AudioPersent = 0;
        }
        private void Button_SaveVideo(object sender, RoutedEventArgs e) {
            //SaveStream(0);
            var dialog = new SaveFileDialog();
            dialog.Filter =
                $"{App.Lang.Files.mkv}|*.mkv|" +
                $"{App.Lang.Files.mp4}|*.mp4|" +
                $"{App.Lang.Files.webm}|*.webm|" +
                $"{App.Lang.Files.mov}|*.mov|" +
                $"{App.Lang.Files.flv}|*.flv";
            dialog.DefaultExt = Data.selectedVideo.video_ext.ToLower();
            dialog.FileName = Path.ChangeExtension(Path.GetFileName(Data.TargetFile), dialog.DefaultExt);
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                var target = dialog.FileName;
                Download_Start_Native(DownloadType.Video, target);
            }
        }
        private void Button_SaveAudio(object sender, RoutedEventArgs e) {
            //SaveStream(1);
            var dialog = new SaveFileDialog();
            dialog.Filter =
                $"{App.Lang.Files.opus}|*.opus|" +
                $"{App.Lang.Files.aac}|*.aac|" +
                $"{App.Lang.Files.m4a}|*.m4a|" +
                $"{App.Lang.Files.mp3}|*.mp3|" +
                $"{App.Lang.Files.vorbis}|*.vorbis|" +
                $"{App.Lang.Files.alac}|*.alac|" +
                $"{App.Lang.Files.flac}|*.flac|" +
                $"{App.Lang.Files.wav}|*.wav";
            dialog.DefaultExt = Data.selectedAudio.acodec.ToLower();
            dialog.FileName = Path.ChangeExtension(Path.GetFileName(Data.TargetFile), dialog.DefaultExt);
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                var target = dialog.FileName;
                Download_Start_Native(DownloadType.Audio, target);
            }
        }
        private void Button_ExplorerTarget(object sender, RoutedEventArgs e) {
            Util.Explorer(Data.TargetFile);
        }
        private void Button_Cancel(object sender, RoutedEventArgs e) {
            if (Data.IsDownload) {
                Data.IsAbouted = true;
                foreach (var dlp in RunningDLP) {
                    dlp.Close();
                }
            }
        }
        private void Button_Download(object sender, RoutedEventArgs e) {
            //Download_Start();
            Download_Start_Native();
        }
        public enum DownloadType { Normal, Video, Audio, Thumbnail, Subtitle }
        private async void Download_Start_Native(DownloadType type = DownloadType.Normal, string target = "") {
            Data.CanCancel = false;
            Data.IsAbouted = false;
            if (Data.IsDownload) {
                Data.IsAbouted = true;
                foreach (var dlp in RunningDLP) {
                    dlp.Close();
                }
            } else {
                var overwrite = true;
                RunningDLP.Clear();
                //如果文件已存在
                if (File.Exists(Data.TargetFile) && type == DownloadType.Normal) {
                    var mb = System.Windows.Forms.MessageBox.Show(
                        $"{App.Lang.Dialog.FileExist}\n",
                        $"{App.AppName}",
                        MessageBoxButtons.YesNo);
                    overwrite = mb == System.Windows.Forms.DialogResult.Yes;
                    if (!overwrite) return; //不要复写
                }
                Data.IsDownload = true;

                //进度更新为0
                ClearStatus();
                try {
                    await Task.Run(() => { // CS1998: Await the main download task
                        var dlp = new DLP(Data.Url);
                        List<Task> tasks = new();
                    tasks.Add(Task.Run(() => {
                        //var dlp = new DLP(Data.Url);
                        RunningDLP.Add(dlp);
                        dlp.IsLive = Data.Video.is_live;
                        var vid = type switch {
                            DownloadType.Video => Data.selectedVideo.format_id,
                            DownloadType.Audio => Data.selectedAudio.format_id,
                            _ => $"{Data.selectedVideo.format_id}+{Data.selectedAudio.format_id}"
                        };
                        dlp
                        .Temp(GetTempPath)
                        .LoadConfig(Data.selectedConfig.file)
                        .MTime(Data.ModifiedType)
                        .Cookie(Data.CookieType, Data.NeedCookie)
                        .Proxy(Data.ProxyUrl, Data.ProxyEnabled)
                        .UseAria2(Data.UseAria2)
                        .LimitRate(Data.LimitRate)
                        .DownloadSections(Data.TimeRange);
                        if (Data.selectedChapter != null) {
                            dlp.SplitChapters(Data.selectedChapter, Data.TargetFile);
                        }

                        switch (type) {
                            case DownloadType.Video:
                                dlp
                                .EmbedChapters(Data.EmbedChapters)
                                .Thumbnail(Data.SaveThumbnail, Data.TargetFile, Data.EmbedThumbnail)
                                .Subtitle(Data.selectedSub.key, Data.TargetFile, Data.EmbedSubtitles)
                                .DownloadVideo(vid, Data.selectedVideo.video_ext, target);
                                break;
                            case DownloadType.Audio:
                                dlp
                                .EmbedChapters(Data.EmbedChapters)
                                .Thumbnail(Data.SaveThumbnail, Data.TargetFile, Data.EmbedThumbnail)
                                .DownloadAudio(vid, target);
                                break;
                            case DownloadType.Subtitle:
                                dlp.DownloadSubtitle(Data.selectedSub.key, target);
                                break;
                            default:
                                dlp
                                .EmbedChapters(Data.EmbedChapters)
                                .Thumbnail(Data.SaveThumbnail, Data.TargetFile, Data.EmbedThumbnail)
                                .Subtitle(Data.selectedSub.key, Data.TargetFile, Data.EmbedSubtitles)
                                .DownloadFormat(vid, Data.TargetFile, Data.OriginExt);
                                break;
                        }
                        var repoter = new StatusRepoter(Data);
                        repoter.type = type switch {
                            DownloadType.Video => 1,
                            DownloadType.Audio => 2,
                            _ => 0
                        };
                        dlp.Exec(std => {
                            repoter.GetStatus(std);
                        });
                    }));
                    //WaitAll Downloads, Merger Video and Audio
                    Data.CanCancel = true;
                    Task.WaitAll(tasks.ToArray());
                    if (!Data.IsAbouted) {
                        Data.DNStatus_Infos["Status"] = App.Lang.Status.Done;

                        //post-process
                        Dictionary<string, string> files = new Dictionary<string, string>();
                        foreach (string donepath in dlp.Files) {
                            if (File.Exists(donepath)) {
                                if (donepath.isVideo()) {
                                    files["video"] = donepath;
                                }
                                if (donepath.isImage()) {
                                    files["thumb"] = donepath;
                                }
                                if (Data.ModifiedType == ModifiedType.Upload) {
                                    if (DateTimeOffset.TryParseExact(Data.Video.upload_date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset tryDate)) {
                                        File.SetLastWriteTimeUtc(donepath, tryDate.DateTime);
                                    }
                                }
                            }
                        }

                        //Send notification when download completed
                        try {
                            if (Data.UseNotifications) {
                                if (!string.IsNullOrEmpty(Data.PathNotify)) Util.NotifySound(Data.PathNotify);
                                var toast = new ToastContentBuilder()
                                    .AddText(Data.Video.title)
                                    .AddText(App.Lang.Dialog.DownloadCompleted)
                                    .AddAudio(new ToastAudio() {
                                        Silent = true,
                                        Loop = false,
                                        Src = new Uri("ms-winsoundevent:Notification.Default")
                                    });
                                if (files.ContainsKey("video")) {
                                    toast.AddButton(
                                        new ToastButton()
                                        .SetContent(App.Lang.Dialog.OpenFolder)
                                        .AddArgument("action", "browse")
                                        .AddArgument("file", files["video"])
                                        .SetBackgroundActivation()
                                    );
                                }
                                if (files.TryGetValue("thumb", out string? thumbPath) && !string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath)) {
                                    try {
                                        toast.AddAppLogoOverride(new Uri(thumbPath));
                                    } catch (UriFormatException) { /* Log or handle */ }
                                      catch (ArgumentNullException) { /* Log or handle */ }
                                }
                                toast.AddButton(
                                    new ToastButton()
                                    .SetContent(App.Lang.Dialog.Close)
                                    .AddArgument("action", "none")
                                    .SetBackgroundActivation()
                                );
                                toast.Show();
                            }
                        } catch (Exception /* ex */) { }
                    }
                    //Clear downloading status
                    // Data.IsDownload = false; // Moved to finally block
                });
            } finally {
                Data.IsDownload = false; // Ensure IsDownload is reset
            }
        }
        }

        private void Button_Browser(object sender, RoutedEventArgs e) {
            if (string.IsNullOrWhiteSpace(Data.TargetName)) {
                var dialog = new FolderBrowserDialog();
                dialog.SelectedPath = Data.TargetPath;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    Data.TargetPath = dialog.SelectedPath;
                }
            } else {
                var dialog = new SaveFileDialog();
                if (!string.IsNullOrEmpty(Data.TargetFile)) {
                    dialog.InitialDirectory = Path.GetDirectoryName(Data.TargetFile);
                    dialog.FileName = Path.GetFileName(Data.TargetFile);
                }
                // If Data.TargetFile is null/empty, InitialDirectory will be default, FileName will be empty.
                // This is standard behavior for SaveFileDialog.
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    Data.RemuxVideo = true;
                    Data.TargetPath = Path.GetDirectoryName(dialog.FileName);
                    Data.TargetName = Path.GetFileName(dialog.FileName);
                    Data.RemuxVideo = false;
                    /*
                    if ((new string[] { ".mp4", ".webm", ".3gp", ".mkv" }).Any(x => Path.GetExtension(dialog.FileName).ToLower() == x)) {
                        Data.TargetName = Path.GetFileName(dialog.FileName);
                    } else {
                        Data.TargetName = Path.GetFileName(dialog.FileName) + ".tmp";
                    }
                    */
                }
            }
        }

        private static Regex RegexValues = new Regex(@"\${(.+?)}", RegexOptions.Compiled);
        private string GetValidFileName(string filename) {
            var regexSearch = new string(Path.GetInvalidFileNameChars());
            return Regex.Replace(filename, string.Format("[{0}]", Regex.Escape(regexSearch)), "_");
        }
        public async void CommandBinding_SaveAs_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) {
            var dialog = new SaveFileDialog();
            if (!string.IsNullOrEmpty(Data.TargetFile)) {
                dialog.InitialDirectory = Path.GetDirectoryName(Data.TargetFile);
            }
            var OrigExt = Path.GetExtension(Data.Thumbnail);
            // Path.GetFileName can handle null, returning null. If Data.TargetFile is null, OrigFileName will be based on null.
            // DefaultExt and Filter will still work. FileName might be just ".jpg" if OrigFileName is null.
            var OrigFileName = Path.ChangeExtension(Path.GetFileName(Data.TargetFile), OrigExt);
            dialog.DefaultExt = ".jpg";
            dialog.Filter = $"{App.Lang.Files.image}|*.jpg;*.webp";
            dialog.FileName = Path.ChangeExtension(OrigFileName, ".jpg");
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                await DownloadThumbnail(dialog.FileName); // Await the async method
            }
        }
        private async Task DownloadThumbnail(string toFile) { // Make method async Task
            var origExt = Path.GetExtension(Data.Thumbnail);
            var origin = Path.ChangeExtension(Data.TargetFile, origExt);
            //var target = Path.ChangeExtension(Data.TargetFile, ".jpg");
            var target = toFile;
            var progress = new Progress<double>(percentage => {
                Debug.Write($"\rDownloading... {percentage:0.00}%");
            });
            await Web.Download(Data.Thumbnail, origin, progress, Data.ProxyEnabled ? Data.ProxyUrl : null);
            //convert to target ext
            if (Path.GetExtension(origin).ToLower() != Path.GetExtension(target)) {
                FFMPEG.DownloadUrl(origin, target);
                File.Delete(origin);
            }
        }

        private void CommandBinding_SaveAs_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e) {
            e.CanExecute = !string.IsNullOrWhiteSpace(Data.Thumbnail);
        }

        private void Button_Subtitle(object sender, RoutedEventArgs e) {
            var dialog = new SaveFileDialog();
            if (!string.IsNullOrEmpty(Data.TargetFile)) {
                dialog.InitialDirectory = Path.GetDirectoryName(Data.TargetFile);
            }
            //dialog.Filter = "SubRip | *.srt";
            //dialog.DefaultExt = Data.selectedSub.key + ".srt";
            dialog.DefaultExt = ".srt";
            dialog.Filter =
                $"{App.Lang.Files.srt}|*.srt|" +
                $"{App.Lang.Files.ass}|*.ass|" +
                $"{App.Lang.Files.vtt}|*.vtt|" +
                $"{App.Lang.Files.lrc}|*.lrc|" +
                $"{App.Lang.Files.ttml}|*.ttml|" +
                $"{App.Lang.Files.srv3}|*.srv3|" +
                $"{App.Lang.Files.srv2}|*.srv2|" +
                $"{App.Lang.Files.srv1}|*.srv1|" +
                $"{App.Lang.Files.json3}|*.json3";
            //dialog.FileName = Path.ChangeExtension(Path.GetFileName(Data.TargetFile), Data.selectedSub.key + ".srt");
            dialog.FileName = Path.ChangeExtension(Data.TargetFile, null);
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                var target = dialog.FileName;
                Debug.WriteLine(dialog.FileName, "DIALOG");
                //var target = Path.ChangeExtension(dialog.FileName, ".srt");
                //FFMPEG.DownloadUrl(Data.selectedSub.url, target);
                Download_Start_Native(DownloadType.Subtitle, target);
            }
        }

        private void MenuItem_About_Click(object sender, RoutedEventArgs e) {
            var win = new About();
            win.Owner = GetWindow(this);
            win.ShowDialog();
        }

        private void Button_Release(object sender, RoutedEventArgs e) {
            var win = new Release();
            win.Owner = GetWindow(this);
            win.ShowDialog();
        }

        private void Window_Closed(object sender, EventArgs e) {
            Data.Left = Left;
            Data.Top = Top;
            Data.Width = Width;
            Data.Height = Height;
        }
        void ComboBox_TextChanged(object sender, TextChangedEventArgs e) {
            var combo = sender as System.Windows.Controls.ComboBox;
            if (combo != null) {
                if (combo.SelectedIndex == -1) {
                    Data.PathTEMP = combo.Text; // combo.Text can be null if editable ComboBox is empty
                } else {
                    Data.PathTEMP = combo.SelectedValue?.ToString() ?? string.Empty;
                }
            }
        }

        private void ToggleButton_Checked(object sender, RoutedEventArgs e) {
            var b = sender as ToggleButton;
            if (b.IsChecked == true) {
                var menu = new List<MenuDataItem>() {
                    (App.Lang.Main.TemporaryTarget, () => { Data.PathTEMP = "%YTDLPGUI_TARGET%"; }),
                    (App.Lang.Main.TemporaryLocale, () => { Data.PathTEMP = "%YTDLPGUI_LOCALE%"; }),
                    (App.Lang.Main.TemporarySystem, () => { Data.PathTEMP = "%TEMP%"; }),
                    ("-"),
                    (App.Lang.Main.TemporaryBrowse, () => {
                        var dialog = new FolderBrowserDialog();
                        dialog.SelectedPath = GetEnvPath(Data.PathTEMP);
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                            Data.PathTEMP = dialog.SelectedPath;
                        }
                    })
                };
                Controls.Menu.Open(menu, b, MenuPlacement.BottomLeft);
            }
        }

        private void ToggleButton_Checked_Sound(object sender, RoutedEventArgs e) {
            var b = sender as ToggleButton;
            if (b.IsChecked == true) {
                var menu = new List<MenuDataItem>() {
                    (App.Lang.Main.SoundSystem, () => { Data.PathNotify = ""; }),
                    ("-"),
                    (App.Lang.Main.SoundBrowse, () => {
                        var dialog = new OpenFileDialog();
                        var dirname = Path.GetDirectoryName(Data.PathNotify);
                        Debug.WriteLine(dirname);
                        if (Directory.Exists(dirname)) {
                            dialog.InitialDirectory = dirname;
                            if (File.Exists(Data.PathNotify)) {
                                dialog.FileName = Path.GetFileName(Data.PathNotify);
                            }
                        } else {
                            dialog.InitialDirectory = App.AppPath;
                        }
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                            Data.PathNotify = dialog.FileName;
                        }
                    })
                };
                Controls.Menu.Open(menu, b, MenuPlacement.BottomLeft);
            }
        }

        private void Button_PlayNotify(object sender, RoutedEventArgs e) {
            Util.NotifySound(Data.PathNotify);
        }

        private void TextBoxNumber_Changed(object sender, EventArgs e) {
            if (Data.Scale == 0) {
                Data.Scale = 100;
            } else if (Data.Scale < 80) {
                Data.Scale = 80;
            } else if (Data.Scale > 200) {
                Data.Scale = 200;
            }
            ChangeScale(Data.Scale);
        }
    }
    public class LanguageConverter :IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            // 检查输入值是否为字符串
            if (!(value is string key))
                return value;

            // 利用反射机制查询 Lang 物件是否包含指定的 key 属性
            var Lang = App.Lang.Status;
            var propertyInfo = Lang.GetType().GetProperty(key, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            // 如果 Lang 物件不包含指定的 key 属性，则返回空字符串
            if (propertyInfo == null)
                return key;

            // 如果 Lang 物件包含指定的 key 属性，则返回相应的值
            return propertyInfo.GetValue(Lang)?.ToString() ?? key;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            // ConvertBack 未实现，因为此转换器仅用于单向绑定
            throw new NotImplementedException();
        }
    }
}
