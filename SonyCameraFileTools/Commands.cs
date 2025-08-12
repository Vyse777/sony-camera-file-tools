using CommandLine;
using Microsoft.Extensions.Logging;

namespace SonyCameraFileTools;

internal interface IApplicationOptions
{
    [Option('l', "log-level", Required = false, Default = LogLevel.Information,
        HelpText = "Set application log level. Default is Information.")]
    public LogLevel LogLevel { get; set; }
}

[Verb("video-renamer", HelpText = "Run the video renamer program.")]
public class VideoRenamerOptions : IApplicationOptions
{
    [Option('u', "unsorted-path", Required = true, HelpText = "The directory to run the video renamer on.")]
    public required string OperatingDirectory { get; set; }

    [Option('s', "sorted-path", Required = true,
        HelpText = "The directory to store the sorted files in. This directory will be created if it does not exist.")]
    public required string SortedDirectoryPath { get; set; }

    public LogLevel LogLevel { get; set; }
}

[Verb("photo-renamer", HelpText = "Run the EXIF based photo renamer program.")]
public class PhotoRenamerOptions : IApplicationOptions
{
    [Option('u', "unsorted-path", Required = true, HelpText = "The directory to run the video renamer on.")]
    public required string UnsortedFilesPath { get; set; }

    [Option('s', "sorted-path", Required = true,
        HelpText = "The directory to store the sorted files in. This directory will be created if it does not exist.")]
    public required string SortedDirectoryPath { get; set; }

    public LogLevel LogLevel { get; set; }
}