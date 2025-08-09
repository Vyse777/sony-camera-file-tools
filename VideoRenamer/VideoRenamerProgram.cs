namespace VideoRenamer;

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using FFMpegCore;
using Microsoft.Extensions.Logging;

public class VideoRenamerProgram(ILoggerFactory loggerFactory, string operatingDirectory)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<VideoRenamerProgram>();

    public int Run()
    {
        DetectOsAndConfigureFfmpegBinaryPath();

        var mp4Files = FindAllApplicableFiles(operatingDirectory);
        if (mp4Files.Count == 0)
        {
            logger.LogInformation(
                "No MP4 files matching criteria for renaming found in the specified directory '{OperatingDirectory}'",
                operatingDirectory);
            return 0;
        }

        logger.LogInformation(
            "Found {Mp4FilesCount} MP4 files in {OperatingDirectory}. Starting metadata check and renaming process...",
            mp4Files.Count, operatingDirectory);

        foreach (var file in mp4Files)
        {
            logger.LogInformation("{FileFullName}", file.FullName);
            logger.LogInformation("Checking for creation_time metadata...");
            var mediaInfo = FFProbe.Analyse(file.FullName);
            if (mediaInfo.Format.Tags?.TryGetValue("creation_time", out var creationTimeString) is not true)
            {
                logger.LogInformation(
                    "creation_time metadata for the file at path '{FileFullName}' was not found. Moving on...",
                    file.FullName);
                continue;
            }

            logger.LogInformation("Discovered creation_time metadata is: {CreationTimeString}", creationTimeString);

            // Just a DateTime parse will initially try to parse the date to the 'local' timezone.
            // "Local" differs based on the execution environment.
            // Using DateTimeOffset, we cna specify the offset to ensure it always uses AZ's UTC-7 offset
            var arizonaCreationDateTime = DateTimeOffset.Parse(creationTimeString).ToOffset(TimeSpan.FromHours(-7));
            var newFileName = arizonaCreationDateTime.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) +
                              ".MP4";
            file.MoveTo(file.DirectoryName + "/" + newFileName);
            logger.LogInformation("File moved to: {FileFullName}", file.FullName);
        }

        logger.LogInformation(
            "Application completed. Please check the directory {OperatingDirectory} for renamed files.",
            operatingDirectory);
        return 0;
    }

    private void DetectOsAndConfigureFfmpegBinaryPath()
    {
        logger.LogDebug("Detecting OS environment");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            logger.LogDebug("Mac OSX environment detected");
            GlobalFFOptions.Configure(options =>
                options.BinaryFolder =
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/Resources/macos/"
            );
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            logger.LogDebug("Linux/Unix environment detected");
            GlobalFFOptions.Configure(options =>
                options.BinaryFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) +
                                       "/Resources/linux/"
            );
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            logger.LogError("Windows Detected.");
            throw new NotSupportedException("Windows platform not supported for this application at this time.");
        }
        else
        {
            logger.LogError("Unknown OS detected.");
            throw new NotSupportedException("This OS is not supported for this application at this time.");
        }
    }

    // Find all MP4 files in the specified directory
    // Sony camera video files start with a C, this is also included as a filter to ensure already renamed files are not included in the run
    private List<FileInfo> FindAllApplicableFiles(string directoryPath)
    {
        logger.LogDebug("Checking for all applicable MP4 files in the specified directory: {DirectoryPath}",
            directoryPath);
        try
        {
            var directory = new DirectoryInfo(directoryPath);

            if (directory.Exists)
                return directory.GetFiles("C*.MP4", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    MatchCasing = MatchCasing.CaseInsensitive,
                }).ToList();

            logger.LogError("Error: Directory {DirectoryPath} does not exist.", directoryPath);
            return [];
        }
        catch (Exception ex)
        {
            logger.LogError("Error while searching for MP4 files: {ExMessage}", ex.Message);
            return [];
        }
    }
}