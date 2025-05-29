﻿using System.Threading.Tasks;

using BlackBytesBox.Distributed.bbdist.Commands;
using BlackBytesBox.Distributed.bbdist.Extensions.SpectreHostExtensions;
using BlackBytesBox.Distributed.bbdist.Serilog;
using BlackBytesBox.Distributed.bbdist.Services;
using BlackBytesBox.Distributed.bbdist.Spectre;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Spectre.Console.Cli;

namespace BlackBytesBox.Distributed.bbdist
{
    public class Program
    {
        public static LoggingLevelSwitch levelSwitch = new LoggingLevelSwitch(LogEventLevel.Verbose);
        public static async Task<int> Main(string[] args)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Warning;

            var loggconfig = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .Enrich.FromLogContext()
                .WriteTo.Console(theme: Theme.ClarionDusk)
                .CreateLogger();

            Log.Logger = loggconfig;

            // Build the host.
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register shared services.
                    services.AddSingleton<IOsVersionService, OsVersionService>();
                    services.AddSingleton<ISolutionProjectService, SolutionProjectService>();
                })
                .AddCommandAppHostedService(config =>
                {
                    config.SetApplicationName("bbdist");
                    config.AddCommand<DumpCommand>("dump").WithDescription("The dump command.").WithExample(new[] { "dump", "osversion" }).WithExample(new[] { "dump", "envars" }).WithExample(new[] { "dump", "osversion", "--loglevel verbose", "--forceSuccess true" }); ;
                    config.AddCommand<VsCodeCommand>("vscode").WithDescription("Installs vscode.").WithExample(new[] { "vscode", "--loglevel verbose", "--forceSuccess true" }); ;
                    config.AddCommand<SlnCommand>("sln").WithDescription("Read solution information.").WithExample(new[] { "--solution", "--loglevel verbose", "--forceSuccess true" }); ;
                    config.AddCommand<CsProjCommand>("csproj").WithDescription("Retrieve project property information from a csproj file.").WithExample(new[] { "--location <file-path>", "--property <property-name>", "--loglevel verbose", "--forceSuccess true" });
                }, args).UseSerilog(Log.Logger).UseConsoleLifetime(e => { e.SuppressStatusMessages = true; })
                ;

            var app = host.Build();

            await app.StartAsync();
            await app.WaitForShutdownAsync();

            // Capture the exit code from the shared ExitCodeHolder.
            int exitCode = CommandAppHostedService.CommandAppExitCode ?? -3;
            if (exitCode == 0)
            {
                Log.Logger.Information("Execution succeeded with exit code {ExitCode}", exitCode);
            }
            else
            {
                Log.Logger.Error("Command exited with error exit code {ExitCode}", exitCode);
            }

#if NET6_0_OR_GREATER
            await Log.CloseAndFlushAsync();

#else
            Log.CloseAndFlush();
#endif

            app.Dispose();

            // Return the exit code.
            return exitCode;
        }
    }
}