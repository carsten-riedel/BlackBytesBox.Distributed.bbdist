using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Spectre.Console.Cli;

using BlackBytesBox.Distributed.bbdist.Services;
using BlackBytesBox.Distributed.bbdist.Spectre;
using Serilog.Events;
using System.ComponentModel;
using Spectre.Console;

namespace BlackBytesBox.Distributed.bbdist.Commands
{

    /// <summary>
    /// A concrete abortable command that demonstrates asynchronous, cancellation-aware work.
    /// After 5 seconds it returns success (0), and if aborted it returns 99.
    /// </summary>
    public class TemplateCommand : CancellableCommand<TemplateCommand.Settings>
    {

        private readonly ILogger<TemplateCommand> _logger;

        // The base error code used when returning error exit codes.
        private int defaultErrorCode = 10;

        public class Settings : CommandSettings
        {
            public string Name { get; set; } = "World";

            /// <summary>
            /// Gets the list of input files to process.
            /// </summary>
            /// <remarks>
            /// Specify the option multiple times:
            ///   myapp process --input file1.txt --input file2.txt  
            /// Spectre.Console will collect each occurrence into this array.  
            /// </remarks>
            [CommandOption("-i|--input <INPUT>")]
            [Description("One or more input file paths.")]
            public string[] Input { get; init; } = Array.Empty<string>();

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

            public override ValidationResult Validate()
            {
                if (Input is null || Input.Length == 0)
                {
                    return ValidationResult.Error("You must specify at least one --input");
                }

                return ValidationResult.Success();
            }
        }

        public TemplateCommand(ILogger<TemplateCommand> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            Program.levelSwitch.MinimumLevel = settings.MinLogLevel;
            _logger.LogInformation("{CommandName} command started.", context.Name);
            _logger.LogWarning("{CommandName} command started.", context.Name);

            try
            {
                // Run for 5 seconds unless canceled.
                int totalSeconds = 5;
                for (int i = 0; i < totalSeconds; i++)
                {
                    _logger.LogInformation("Working... {Second}s", i + 1);
                    await Task.Delay(1000, cancellationToken);
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
