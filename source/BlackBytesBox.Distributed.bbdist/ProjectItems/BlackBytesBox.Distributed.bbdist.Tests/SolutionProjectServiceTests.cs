using System.Runtime.Intrinsics.X86;

using global::BlackBytesBox.Distributed.bbdist.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BlackBytesBox.Distributed.bbdist.Tests
{
    [TestClass]
    public sealed class SolutionProjectServiceTests
    {
        private static IHost? host;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            // This method is called once for the test class, before any tests of the class are run.
            host = Host.CreateDefaultBuilder()
                .ConfigureLogging((ctx, configureLogging) =>
                {
                    configureLogging.ClearProviders();
                    configureLogging.AddConsole();
                })
                .ConfigureServices((ctx, services) =>
                {
                    services.AddTransient<ISolutionProjectService, SolutionProjectService>();
                })
                .Build();
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            // This method is called once for the test class, after all tests of the class are run.
            host?.Dispose();
        }

        [TestInitialize]
        public void TestInit()
        {
            // This method is called before each test method.
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // This method is called after each test method.
        }

        [DataTestMethod]
        [DataRow(100)]
        public async Task TestSolutionProjectServiceIntegration(int delay)
        {
            // Resolve the service from the host's service provider.
            if (host is null) throw new InvalidOperationException("Host is not initialized.");
            ISolutionProjectService solutionProjectService = host.Services.GetRequiredService<ISolutionProjectService>();

            // Call the service method with a short delay and a cancellation token.
            var result = await solutionProjectService.GetCsProjAbsolutPathsFromSolutions(@"C:\dev\github.com\carsten-riedel\BlackBytesBox.Distributed.bbdist\source\BlackBytesBox.Distributed.bbdist.sln", CancellationToken.None);


        }


    }
}