using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization; // Required for CultureInfo.InvariantCulture

namespace yt_dlp_gui.Models {
    public class FormatFilesizeConverter : JsonConverter<Format> {
        // Removed CanConvert method, as it cannot override the sealed method in JsonConverter<Format>

        public override void WriteJson(JsonWriter writer, Format value, JsonSerializer serializer) {
            throw new NotImplementedException("Serialization back to JSON is not implemented for FormatFilesizeConverter.");
        }

        public override Format ReadJson(JsonReader reader, Type objectType, Format existingValue, bool hasExistingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.Null) {
                return null;
            }

            JObject jObject = JObject.Load(reader);
            Format format = new Format();

            // Populate standard properties
            format.format_id = jObject["format_id"]?.Value<string>() ?? string.Empty;
            format.format_note = jObject["format_note"]?.Value<string>() ?? string.Empty;
            format.ext = jObject["ext"]?.Value<string>() ?? string.Empty;
            format.vcodec = jObject["vcodec"]?.Value<string>() ?? string.Empty;
            format.acodec = jObject["acodec"]?.Value<string>() ?? string.Empty;
            format.container = jObject["container"]?.Value<string>() ?? string.Empty;
            format.audio_ext = jObject["audio_ext"]?.Value<string>() ?? "none";
            format.video_ext = jObject["video_ext"]?.Value<string>() ?? "none";
            format.format = jObject["format"]?.Value<string>() ?? string.Empty; // The string 'format' property
            format.resolution = jObject["resolution"]?.Value<string>() ?? string.Empty;
            format.info = jObject["info"]?.Value<string>() ?? string.Empty;
            format.dynamic_range = jObject["dynamic_range"]?.Value<string>();

            // Nullable decimal properties
            format.asr = jObject["asr"]?.Value<decimal?>();
            format.fps = jObject["fps"]?.Value<decimal?>();
            format.height = jObject["height"]?.Value<decimal?>();
            format.width = jObject["width"]?.Value<decimal?>();
            format.tbr = jObject["tbr"]?.Value<decimal?>();
            format.vbr = jObject["vbr"]?.Value<decimal?>();
            format.abr = jObject["abr"]?.Value<decimal?>();
            format.preference = jObject["preference"]?.Value<decimal?>();

            // Filesize processing
            string filesizeStr = jObject["filesize"]?.Value<string>();
            if (!string.IsNullOrEmpty(filesizeStr)) {
                if (filesizeStr.StartsWith("~")) {
                    format.isFilesizeApprox = true;
                    if (long.TryParse(filesizeStr.Substring(1), NumberStyles.Any, CultureInfo.InvariantCulture, out long parsedSize)) {
                        format.filesize = parsedSize;
                    } else {
                        format.filesize = null;
                    }
                } else {
                    format.isFilesizeApprox = false;
                    if (long.TryParse(filesizeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsedSize)) {
                        format.filesize = parsedSize;
                    } else {
                        format.filesize = null;
                    }
                }
            } else {
                format.filesize = null;
                format.isFilesizeApprox = false;
            }

            // Enum Type property
            string typeStr = jObject["type"]?.Value<string>();
            if (!string.IsNullOrEmpty(typeStr) && Enum.TryParse<FormatType>(typeStr, true, out FormatType parsedType)) {
                format.type = parsedType;
            } else {
                // Attempt to infer type based on codecs if 'type' field is missing or invalid
                bool vcodecPresent = !string.IsNullOrEmpty(format.vcodec) && format.vcodec.ToLowerInvariant() != "none";
                bool acodecPresent = !string.IsNullOrEmpty(format.acodec) && format.acodec.ToLowerInvariant() != "none";

                if (vcodecPresent && acodecPresent) {
                    format.type = FormatType.package;
                } else if (vcodecPresent) {
                    format.type = FormatType.video;
                } else if (acodecPresent) {
                    format.type = FormatType.audio;
                } else {
                    format.type = FormatType.other; // Default or further logic needed
                }
            }
            return format;
        }
    }
}
