using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhotoMover
{
    public class CustomOutputLocation
    {
        private readonly ILogger<CustomOutputLocation> _logger;
        private readonly IConfiguration _config;

        private readonly string SourceLocation;
        private readonly string PhotoDestLocation;
        private readonly string VideoDestLocation;

        internal static string[] PictureExtensions = new string[] { ".jpg", ".gif", ".png", ".heic", ".jpeg" };
        internal static string[] VideoExtensions = new string[] { ".mov", ".avi", ".mp4", ".m4v", ".wmv", ".3gp" };

        public CustomOutputLocation(ILogger<CustomOutputLocation> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            SourceLocation = _config["SourceLocation"];
            PhotoDestLocation = _config["PhotoDestLocation"];
            VideoDestLocation = _config["VideoDestLocation"];

        }
        public string BuildOutputPath(FileInfo fileInfo)
        {
            // build output path

            string destLocationDir = String.Empty;

            if (PictureExtensions.Contains(fileInfo.Extension.ToLower()))
            {
                // move picture
                destLocationDir = PhotoDestLocation;
            }
            else if (VideoExtensions.Contains(fileInfo.Extension.ToLower()))
            {
                // move video
                destLocationDir = VideoDestLocation;
            }
            if (string.IsNullOrEmpty(destLocationDir))
            {
                _logger.LogWarning($"File {fileInfo.FullName} not processed, not a supported photo or video type");
                return string.Empty;
            }

            string destFileLocationPath = string.Empty;
            try
            {
                var dateTaken = GetDateTakenFromImage(fileInfo.FullName, _logger); // fileInfo.LastWriteTime;
                if(!dateTaken.HasValue)
                {
                    dateTaken = fileInfo.LastWriteTime;
                }
                if (dateTaken.HasValue)
                {
                    string season = $"{dateTaken.Value.ToString("MM")}-{dateTaken.Value.ToString("MMM")}";
                    destFileLocationPath = Path.Combine(destLocationDir,
                                                Path.Combine($"{dateTaken.Value.Year}",
                                                    Path.Combine(season, fileInfo.Name)));
                }
            }
            catch (Exception exc)
            {
                _logger.LogError($"Exception building output path for {fileInfo.Name}: {exc.Message}");
            }
            return destFileLocationPath;
        }

        private static Regex r = new Regex(":");

        //retrieves the datetime WITHOUT loading the whole image
        public static DateTime? GetDateTakenFromImage(string imagePath, ILogger logger)
        {
            IEnumerable<MetadataExtractor.Directory> metadataDirectories = ImageMetadataReader.ReadMetadata(imagePath);
            DateTime? dateTaken = null;
            try
            {

                // Find the so-called Exif "SubIFD" (which may be null)
                var subIfdDirectory = metadataDirectories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                // Read the DateTime tag value
                dateTaken = subIfdDirectory?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal);
            }
            catch (Exception exc)
            {
                logger.LogError($"Could not extract date metadata from {imagePath} : {exc.Message}");
                if (!dateTaken.HasValue && !imagePath.Contains("jpg"))  // already "debugged" jpg
                {
                    foreach (var directory in metadataDirectories)
                        foreach (var tag in directory.Tags)
                            logger.LogWarning($"{directory.Name} - {tag.Name} = {tag.Description}");
                }
            }
            return dateTaken ?? null;
        }
    }
}
