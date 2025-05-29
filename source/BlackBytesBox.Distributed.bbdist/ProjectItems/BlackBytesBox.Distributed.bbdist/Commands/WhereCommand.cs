using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BlackBytesBox.Distributed.bbdist.Services.CommandsServices;
using BlackBytesBox.Distributed.bbdist.Spectre;

using Microsoft.Extensions.Logging;

using Serilog.Events;

using Spectre.Console.Cli;



namespace BlackBytesBox.Distributed.bbdist.Commands
{

    /// <summary>
    /// A concrete abortable command that demonstrates asynchronous, cancellation-aware work.
    /// After 5 seconds it returns success (0), and if aborted it returns 99.
    /// </summary>
    public class WhereCommand : CancellableCommand<WhereCommand.Settings>
    {

        private readonly ILogger<WhereCommand> _logger;
        private readonly IWhereCommandService _whereCommandService;

        // The base error code used when returning error exit codes.
        private int defaultErrorCode = 10;

        public class Settings : CommandSettings
        {
            /// <summary>
            /// Gets or sets the comma-separated list of executable file names to search for (e.g. "test.ps1,git.exe,msbuild.exe").
            /// </summary>
            [Description("Comma-separated list of executable names to search for.")]
            [CommandOption("--filenames")]
            public string? Filesnames { get; set; }

            /// <summary>
            /// Gets or sets the comma-separated list of root directories in which to search (e.g. "C:\\,D:\\Tools").
            /// </summary>
            [Description("Comma-separated list of root directories to search.")]
            [CommandOption("--directories")]
            public string? Directories { get; set; }

            /// <summary>
            /// Gets or sets the comma-separated list of directories to skip (e.g. "C:\\Windows,C:\\ProgramData").
            /// </summary>
            [Description("Comma-separated list of directories to skip.")]
            [DefaultValue(null)]
            [CommandOption("--skip-directories")]
            public string? SkipDirectories { get; set; }

            /// <summary>
            /// Gets or sets the PE-file type filter. Only files matching this type will be included; use <c>None</c> to include all.
            /// </summary>
            [Description("PE-file type to filter results; use None to apply no filtering.")]
            [DefaultValue(WhereCommandService.PeFileType.None)]
            [CommandOption("--filter")]
            public WhereCommandService.PeFileType Filter { get; init; }

            /// <summary>
            /// Gets or sets the minimum log level.
            /// Valid values: Verbose, Debug, Information, Warning, Error, Fatal. Default is Warning.
            /// </summary>
            [Description("The minimum log level, valid values: Verbose, Debug, Information, Warning, Error, Fatal. Default is Warning.")]
            [DefaultValue(LogEventLevel.Warning)]
            [CommandOption("-m|--minLogLevel")]
            public LogEventLevel MinLogLevel { get; init; }


            /// <summary>
            /// Gets or sets a value indicating whether the command should ignore errors.
            /// If true, the command always returns 0 regardless of errors.
            /// </summary>
            [Description("If true, any errors encountered will be ignored and a success exit code (0) is returned.")]
            [DefaultValue(false)]
            [CommandOption("-i|--ignoreerrors")]
            public bool IgnoreErrors { get; init; }
        }

        public WhereCommand(ILogger<WhereCommand> logger, IWhereCommandService whereCommandService)
        {
            _logger = logger;
            _whereCommandService = whereCommandService;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            Program.levelSwitch.MinimumLevel = settings.MinLogLevel;
            _logger.LogInformation("{CommandName} command started.", context.Name);

            try
            {
                var allFilesLocation = await _whereCommandService.FindFiles(settings.Filesnames,settings.Directories,settings.SkipDirectories, settings.Filter,cancellationToken);
                List<WhereCommandService.DirFilePeTypeMatch> allFilesLocationWithPeType;
                if (settings.Filter != WhereCommandService.PeFileType.None)
                {
                    allFilesLocationWithPeType = (await _whereCommandService.GetFilesAndPeTypeFromDirectoriesAsync(allFilesLocation)).Where(e => e.PeType == settings.Filter).ToList();
                }
                else
                {
                    allFilesLocationWithPeType = (await _whereCommandService.GetFilesAndPeTypeFromDirectoriesAsync(allFilesLocation)).ToList();
                }

                if (allFilesLocationWithPeType.Count == 0)
                {
                    _logger.LogWarning("{CommandName} command found no files matching the specified criteria.", context.Name);
                    return settings.IgnoreErrors ? 0 : defaultErrorCode + 100 + 1;
                }

                foreach (var item in allFilesLocationWithPeType)
                {
                    Console.WriteLine($"{System.IO.Path.Combine(item.Directory,item.FileName)}");
                }

                _logger.LogInformation("{CommandName} completed normally after 5 seconds..", context.Name);
                return 0;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "{CommandName} command canceled internally.", context.Name);
                return settings.IgnoreErrors ? 0 : defaultErrorCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{CommandName} command encountered an error.", context.Name);
                return settings.IgnoreErrors ? 0 : defaultErrorCode + 1;
            }
        }
    }
}
