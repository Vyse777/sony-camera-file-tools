using System.Globalization;
using System.Runtime.InteropServices;
using FFMpegCore;
using Instances.Exceptions;
using Microsoft.Extensions.Logging;

namespace VideoRenamer;

public class VideoRenamerProgram(
    ILoggerFactory loggerFactory,
    string unsortedFilesDirectoryPath,
    string sortedDirectoryPath)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<VideoRenamerProgram>();

    public int Run()
    {
        var unsortedDirectoryInfo = new DirectoryInfo(unsortedFilesDirectoryPath);
        if (!unsortedDirectoryInfo.Exists)
            throw new DirectoryNotFoundException($"Operating directory {unsortedFilesDirectoryPath} was not found.");
        var sortedDirectoryInfo = CheckForOrCreateDirectory(new DirectoryInfo(sortedDirectoryPath));

        DetectOsAndConfigureFfmpegBinaryPath();

        var mp4Files = FindAllApplicableFiles(unsortedDirectoryInfo);
        if (mp4Files.Count == 0)
        {
            logger.LogInformation(
                "No MP4 files matching criteria for renaming found in the specified directory '{OperatingDirectory}'",
                unsortedFilesDirectoryPath);
            return 0;
        }

        logger.LogInformation(
            "Found {Mp4FilesCount} MP4 files in unsorted directory. Starting metadata check and renaming process...",
            mp4Files.Count);

        foreach (var file in mp4Files)
            try
            {
                var originalFilename = file.Name;
                logger.LogDebug("Processing file {Filename}", originalFilename);

                logger.LogDebug("Checking for creation_time metadata...");
                var mediaInfo = FFProbe.Analyse(file.FullName);
                if (mediaInfo.Format.Tags?.TryGetValue("creation_time", out var creationTimeString) is not true)
                {
                    logger.LogInformation(
                        "creation_time metadata for the file at path '{FileFullName}' was not found. Moving on...",
                        file.FullName);
                    continue;
                }

                logger.LogDebug("Discovered creation_time metadata is: {CreationTimeString}", creationTimeString);

                // Just a DateTime parse will initially try to parse the date to the 'local' timezone.
                // "Local" differs based on the execution environment.
                // Using DateTimeOffset, we can specify the offset to ensure it always uses AZ's UTC-7 offset
                var arizonaCreationDateTime = DateTimeOffset.Parse(creationTimeString).ToOffset(TimeSpan.FromHours(-7));

                // Sorted file structure will always follow /path/to/sorted/directory then /sorted/yyyy-MM/
                var destinationDirectoryInfo = new DirectoryInfo(
                    Path.Combine(
                        sortedDirectoryInfo.FullName,
                        arizonaCreationDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                    )
                );
                if (!destinationDirectoryInfo.Exists)
                {
                    logger.LogDebug(
                        "Year-month directory does not exist yet, creating directory at path: {DestinationDirectory}",
                        destinationDirectoryInfo);
                    destinationDirectoryInfo.Create();
                }

                var newFileName =
                    arizonaCreationDateTime.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) +
                    file.Extension;
                var destinationFileInfo = new FileInfo(Path.Combine(destinationDirectoryInfo.FullName, newFileName));
                logger.LogDebug("New file path will be {DestinationPath}", destinationFileInfo);

                if (destinationFileInfo.Exists)
                {
                    logger.LogWarning(
                        "Destination file already exists: {DestinationPath} Skipping this file {FilePath}",
                        destinationFileInfo, file.FullName);
                    continue;
                }

                file.MoveTo(destinationFileInfo.FullName);
                logger.LogInformation("Processed video file: {OriginalFilename} ---> {NewFilename}", originalFilename,
                    file.Name);
            }
            catch (IOException ioEx)
            {
                logger.LogError(ioEx, "I/O error while processing file '{FileName}'. Skipping.", file.Name);
            }
            catch (InstanceFileNotFoundException e)
            {
                logger.LogError(e,
                    "Error attempting to read metadata - likely path to FFProbe is incorrect. Terminating application");
                return 1;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while processing file '{FileName}'. Skipping.", file.Name);
            }

        logger.LogInformation("Successfully processed {Count} files", mp4Files.Count);
        return 0;
    }

    private DirectoryInfo CheckForOrCreateDirectory(DirectoryInfo directoryInfo)
    {
        if (directoryInfo.Exists) return directoryInfo;

        logger.LogInformation("Sorted directory not found: {DirectoryPath}. Attempting to create the directory...",
            directoryInfo);
        directoryInfo.Create();
        logger.LogInformation("Sorted directory created");

        return directoryInfo;
    }

    private void DetectOsAndConfigureFfmpegBinaryPath()
    {
        logger.LogDebug("Detecting OS environment");
        var pathToApplicationResources =
            new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Resources"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            logger.LogDebug("Mac OSX environment detected");
            GlobalFFOptions.Configure(options =>
                options.BinaryFolder = Path.Combine(pathToApplicationResources.FullName, "macos")
            );
            logger.LogDebug("FFProbe binary path set to {Path}",
                Path.Combine(pathToApplicationResources.FullName, "macos"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            logger.LogDebug("Linux/Unix environment detected");
            GlobalFFOptions.Configure(options =>
                options.BinaryFolder = Path.Combine(pathToApplicationResources.FullName, "linux")
            );

            logger.LogDebug("FFProbe binary path set to {Path}",
                Path.Combine(pathToApplicationResources.FullName, "macos"));
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
    private List<FileInfo> FindAllApplicableFiles(DirectoryInfo directoryInfo)
    {
        logger.LogDebug("Checking for all applicable MP4 files in the specified directory: {DirectoryPath}",
            directoryInfo.FullName);
        try
        {
            return directoryInfo.GetFiles("C*.MP4", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                MatchCasing = MatchCasing.CaseInsensitive
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError("Error while searching for MP4 files: {ExMessage}", ex.Message);
            return [];
        }
    }
}