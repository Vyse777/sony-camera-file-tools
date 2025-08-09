using System.Globalization;
using Microsoft.Extensions.Logging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Directory = System.IO.Directory;

namespace PhotoRenamer;

public class PhotoRenamerProgram(
    ILoggerFactory loggerFactory,
    string unsortedFilesDirectoryPath,
    string sortedDirectoryPath
)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<PhotoRenamerProgram>();

    public int Run()
    {
        var unsortedDirectoryInfo = new DirectoryInfo(unsortedFilesDirectoryPath);
        if (!unsortedDirectoryInfo.Exists)
            throw new DirectoryNotFoundException($"Operating directory {unsortedFilesDirectoryPath} not found.");

        var sortedDirectoryInfo = CheckForOrCreateDirectory(sortedDirectoryPath);
        var unsortedPhotoFiles = FindAllApplicablePhotoFiles(unsortedDirectoryInfo);

        if (unsortedPhotoFiles.Count == 0)
        {
            logger.LogInformation("No files matching rename criteria found in '{UnsortedDirectoryPath}'.",
                unsortedDirectoryInfo.FullName);
            return 0;
        }

        logger.LogInformation("Found {Count} files to process in '{UnsortedDirectoryPath}'.", unsortedPhotoFiles.Count,
            unsortedDirectoryInfo.FullName);

        foreach (var file in unsortedPhotoFiles)
        {
            try
            {
                logger.LogInformation("Processing file: {FileName}", file.Name);

                if (!TryGetCreatedAtExifData(file, out var photoCreatedAtDateTime))
                {
                    logger.LogWarning("Could not determine DateTimeOriginal metadata for file '{FileName}'. Skipping.",
                        file.Name);
                    continue;
                }

                // Sorted file structure will always follow /path/to/sorted/directory then /sorted/yyyy-MM/yyyy-MM-dd HH.mm.ss.fff.fileExtension
                // Example: /some/place/sorted/2069-04/2069-04-20 04.20.42.420.HIF
                var yearMonthString = photoCreatedAtDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var destinationDirectory = Path.Combine(sortedDirectoryInfo.FullName, yearMonthString);

                var newFileName =
                    photoCreatedAtDateTime.ToString("yyyy-MM-dd HH.mm.ss.fff", CultureInfo.InvariantCulture) +
                    file.Extension;
                var destinationPath = Path.Combine(destinationDirectory, newFileName);

                logger.LogDebug("New file path will be {DestinationPath}", destinationPath);

                if (!Directory.Exists($"{sortedDirectoryInfo.FullName}/{yearMonthString}"))
                {
                    logger.LogInformation(
                        "Year-month directory does not exist yet, creating directory at path: {FullName}/{YearMonthString}", sortedDirectoryInfo.FullName, yearMonthString);
                    Directory.CreateDirectory($"{sortedDirectoryInfo.FullName}/{yearMonthString}");
                }

                if (File.Exists(destinationPath))
                {
                    logger.LogWarning(
                        "Destination file already exists: {DestinationPath}. Skipping this file {FilePath}.",
                        destinationPath, file.FullName);
                    ;
                    continue;
                }

                file.MoveTo(destinationPath);
                logger.LogInformation("Moved '{Source}' -> '{Destination}'", file.FullName, destinationPath);
            }
            catch (IOException ioEx)
            {
                logger.LogError(ioEx, "I/O error while processing file '{FileName}'. Skipping.", file.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while processing file '{FileName}'. Skipping.", file.Name);
            }
        }

        logger.LogInformation("Successfully processed {Count} files", unsortedPhotoFiles.Count);
        return 0;
    }

    private DirectoryInfo CheckForOrCreateDirectory(string directoryPath)
    {
        var directoryInfo = new DirectoryInfo(directoryPath);
        if (directoryInfo.Exists) return directoryInfo;

        logger.LogInformation("Sorted directory not found: {DirectoryPath}. Attempting to create the directory...", directoryPath);
        Directory.CreateDirectory(directoryPath);
        logger.LogInformation("Sorted directory created");

        return directoryInfo;
    }

    // Find all files that look like Sony camera photos (e.g., containing "DSC") and are not yet renamed
    // TODO: Review the name - it's doing more than this now with the changes handling the edge case.
    private List<FileInfo> FindAllApplicablePhotoFiles(DirectoryInfo directory)
    {
        try
        {
            // Alright quick explainer for the below code flow:
            // In general, when DSC files come off of an SD card, they are all named accordingly:
            // DSC0001.HIF
            // DSC0002.HIF
            // DSC0003.HIF
            // Etc. Etc.
            // In this case, we have no issues. So we can continue as normal.
            //
            // However, if a user decides to make use of the FTP transfer option on their camera to offload files to a server remotely/automatically (a not-so-uncommon workflow for some photographers),
            // an interrupted upload (network disconnects, battery dies during backup, etc.) might be retried later.
            // On retry, the camera keeps the original attempt on the server (e.g., DSC0001.HIF), and a new file is created (e.g., DSC0001_1.HIF).
            // The "_n" variant (or highest N value for multiple retries) is the official image; the others are essentially incomplete containers of bits.
            // This program, if the below is NOT done, would attempt to rename both variants of the file, containing the identical CreatedAt EXIF data - and so would rename the first, incomplete, file successfully.
            // The second would result in a "file already exists" case and would be skipped.
            // Thus, abandoning the valid file in the unsorted directory while the shitty broken file lives on in the sorted directory.
            // What the below does is if all the files are valid and don't contain retries, we simply return the FileInfo for all of them - yippy
            // If there are any files that contain retries (any that contains "_"), then we do some work to ensure we only take the FileInfo of the highest N value file, assuming that it's complete.

            var allMatchingPhotoFiles = directory.GetFiles("DSC*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                MatchCasing = MatchCasing.CaseInsensitive
            });

            if (!allMatchingPhotoFiles.Any(f => f.Name.Contains('_'))) return allMatchingPhotoFiles.ToList();

            logger.LogDebug(
                "Found files that might have been retried during FTP uploads. Picking the highest N value file for each group of files with the same name.");
            var filesToRename = allMatchingPhotoFiles.Select(f =>
                {
                    if (f.Name.Contains('_'))
                    {
                        return new
                        {
                            // Adjust the filename to what it should be (DSC0001_2.HIF -> DSC0001.HIF) - this becomes our grouping key!
                            ActualFileName = f.Name[..f.Name.LastIndexOf('_')] + f.Extension,
                            OriginalFileName = f.Name,
                            HasUnderscore = true,
                            FileInfoObject = f
                        };
                    }

                    return new
                    {
                        ActualFileName = f.Name,
                        OriginalFileName = f.Name,
                        HasUnderscore = false,
                        FileInfoObject = f
                    };
                })
                .GroupBy(t => t.ActualFileName, StringComparer.OrdinalIgnoreCase)
                .SelectMany(grouping =>
                {
                    // If the count is 1, then we have a grouping of 1, and so we just return the FileInfo object 
                    if (grouping.Count() == 1) return grouping.Select(x => x.FileInfoObject);
                    // Otherwise, we've bucketed more than one file with the same "actual name"
                    return grouping
                        // We only care about the file(s) with underscores 
                        .Where(x => x.HasUnderscore)
                        // Order lexicographically using the original filename (with underscore)
                        .OrderByDescending(x => x.OriginalFileName)
                        // take the highest "_n" valued filename - ignoring the rest
                        .Take(1)
                        // Return only the FileInfo object
                        .Select(x => x.FileInfoObject);
                }).ToList();

            // I don't want to leave around the dead files, and at first glance someone might not know they were broken. The context of this lives within this runtime.
            // Plus, it will get picked up in the next run through of the app, which would be even worse!

            // Get the files that were "filtered out" of the process above
            var possiblyCorruptedFiles = allMatchingPhotoFiles.Except(filesToRename).ToList();
            logger.LogDebug("Found {Count} files that are possibly corrupted.", possiblyCorruptedFiles.Count);

            // TODO: Review if the sorted directory makes more sense. Update accordingly.
            var likelyCorruptedImagesDirectoryPath = unsortedFilesDirectoryPath + "/likely-corrupted";
            if (!Directory.Exists(likelyCorruptedImagesDirectoryPath))
            {
                logger.LogDebug(
                    "A directory containing likely corrupted image files did not exist. Creating it in path {Path}",
                    likelyCorruptedImagesDirectoryPath);
                Directory.CreateDirectory(unsortedFilesDirectoryPath + "/likely-corrupted");
            }

            foreach (var file in possiblyCorruptedFiles)
            {
                try
                {
                    file.MoveTo(unsortedFilesDirectoryPath + "/likely-corrupted/" + file.Name);
                }
                catch (Exception e)
                {
                    logger.LogError(
                        "An unknown error occurred while moving file '{FilePath}' to the likely-corrupted directory. WARNING: Skipping moving this file - but it will remain in the unsorted directory! Error: {Error}",
                        file.FullName, e);
                }
            }

            logger.LogDebug("Moved {Count} files to {Path}", possiblyCorruptedFiles.Count,
                likelyCorruptedImagesDirectoryPath);

            return filesToRename;
        }
        catch (Exception e)
        {
            logger.LogError("An error occurred while searching for files. {Error}", e);
            return [];
        }
    }

    // Try to read DateTimeOriginal (with optional subsecond precision) from EXIF
    private bool TryGetCreatedAtExifData(FileInfo filePath, out DateTime dateTime)
    {
        dateTime = default;
        try
        {
            logger.LogDebug("Opening file '{FilePath}' for reading.", filePath.FullName);
            using var fileStream = File.OpenRead(filePath.FullName);

            logger.LogDebug("Reading EXIF data for file '{FilePath}'...", filePath.FullName);
            var metadataDirectories = ImageMetadataReader.ReadMetadata(fileStream);
            // SubIf contains the created at metadata that we need to rename the file.
            var exifData = metadataDirectories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifData is null)
            {
                logger.LogWarning("No EXIF data found for file '{FilePath}'.", filePath);
                return false;
            }

            // Sony cameras store the datetime metadata already shifted for the offset set in the camera (i.e., not UTC time, and not a DateTimeOffset).
            // So the datetime below is the date and time the image was taken in the timezone set in the camera configuration.
            // There is also a metadata tag named "Time Zone for Original Date" which houses the offset. But this isn't needed for this application unless we wanted to normalize to a timezone spec.
            // I have no fucking clue what happens if the user didn't set the timezone in the camera. Here's hoping they/I do!
            var baseDate = exifData.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal);
            logger.LogDebug("Found DateTimeOriginal for file {FilePath}. Date Time: {DateTime}", filePath.FullName,
                baseDate);

            double milliseconds = 0;
            if (exifData.ContainsTag(ExifDirectoryBase.TagSubsecondTime))
            {
                try
                {
                    logger.LogDebug("Attempting to get subsecond EXIF data/tag value for file '{FilePath}'.",
                        filePath.FullName);
                    milliseconds = exifData.GetDouble(ExifDirectoryBase.TagSubsecondTime);
                    logger.LogDebug("Subsecond EXIF data found for file '{FilePath}'. Data: {Milliseconds}.",
                        filePath.FullName, milliseconds);
                    ;
                }
                catch
                {
                    logger.LogWarning("Unable to get subsecond EXIF data/tag value for file '{FilePath}'.", filePath);
                }
            }

            dateTime = baseDate.AddMilliseconds(milliseconds);
            return true;
        }
        catch
        {
            logger.LogWarning("Unable to read EXIF data for file '{FilePath}'.", filePath);
            return false;
        }
    }
}