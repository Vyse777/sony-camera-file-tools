using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoRenamer;
using VideoRenamer;

namespace SonyCameraFileTools;

internal abstract class Program
{
    public static int Main(string[] args)
    {
        var result = new Parser(with =>
            {
                with.CaseInsensitiveEnumValues = true;
                with.HelpWriter = Console.Error;
            })
            .ParseArguments<VideoRenamerOptions, PhotoRenamerOptions>(args)
            .MapResult(
                (VideoRenamerOptions options) => Start(options),
                (PhotoRenamerOptions options) => Start(options),
                error => 1
            );
        return result;
    }

    private static int Start(IApplicationOptions options)
    {
        // TODO: Console logging is probably ok for now, but Serilog is best since we would be able to write to Console AND a file (Debug to file, Information to console)
        //  Let's switch to that at some point™
        
        var applicationLoggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(options.LogLevel).AddSimpleConsole(loggerOptions =>
            {
                loggerOptions.IncludeScopes = true;
                loggerOptions.SingleLine = true;
                loggerOptions.TimestampFormat = "[HH:mm:ss] ";
            })
        );
        var programLogger = applicationLoggerFactory.CreateLogger<Program>();
        programLogger.LogDebug("Successfully parsed commandline arguments. Starting application based on verb passed.");

        switch (options)
        {
            case VideoRenamerOptions videoRenamerOptions:
            {
                programLogger.LogInformation("Starting Video Renamer program...");

                var result = new VideoRenamerProgram(
                    applicationLoggerFactory,
                    videoRenamerOptions.OperatingDirectory
                ).Run();

                programLogger.LogInformation("Video Renamer program finished.");
                programLogger.LogDebug("Video Renamer program finished with exit code {ExitCode}.", result);
                return result;
            }
            case PhotoRenamerOptions photoRenamerOptions:
            {
                programLogger.LogInformation("Starting Photo Renamer program...");

                var result = new PhotoRenamerProgram(
                    applicationLoggerFactory,
                    photoRenamerOptions.UnsortedFilesPath,
                    photoRenamerOptions.SortedPhotosPath
                ).Run();

                programLogger.LogInformation("Photo Renamer program finished.");
                programLogger.LogDebug("Photo Renamer program finished with exit code {ExitCode}.", result);
                return result;
            }
        }

        programLogger.LogError("Could not determine which verb to invoke. Exiting.");
        return 1;
    }
}