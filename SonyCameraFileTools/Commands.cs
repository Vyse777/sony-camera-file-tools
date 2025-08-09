using CommandLine;
using Microsoft.Extensions.Logging;

namespace SonyCameraFileTools;

interface IApplicationOptions
{
    [Option('l', "log-level", Required = false, Default = LogLevel.Information, HelpText = "Set application log level. Default is Information.")]
    public LogLevel LogLevel { get; set; }
}

[Verb("video-renamer", HelpText = "Run the video renamer program.")]
public class VideoRenamerOptions : IApplicationOptions
{
    [Option('d', "directory", Required = true, HelpText = "The directory to run the video renamer on.")]
    public required string OperatingDirectory { get; set; }

    public LogLevel LogLevel { get; set; }
}

[Verb("photo-renamer", HelpText = "Run the EXIF based photo renamer program.")]
public class PhotoRenamerOptions : IApplicationOptions
{
    [Option('u', "unsorted-path", Required = true, HelpText = "The directory to run the video renamer on.")]
    public required string UnsortedFilesPath { get; set; }

    [Option('s', "sorted-path", Required = true, HelpText = "The directory to run the video renamer on")]
    public required string SortedPhotosPath { get; set; }
    
    public LogLevel LogLevel { get; set; }
}