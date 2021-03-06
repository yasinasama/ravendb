using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;
using Raven.Server.Config;
using Raven.Server.Documents.Handlers.Debugging;
using Raven.Server.ServerWide;
using Raven.Server.Utils;
using Raven.Server.Utils.Cli;
using Sparrow.Json.Parsing;
using Sparrow.Logging;

namespace Raven.Server
{
    public class Program
    {
        private static readonly Logger Logger = LoggingSource.Instance.GetLogger<Program>("Raven/Server");

        public static int Main(string[] args)
        {
            string[] configurationArgs;
            try
            {
                configurationArgs = CommandLineSwitches.Process(args);
            }
            catch (CommandParsingException commandParsingException)
            {
                Console.WriteLine(commandParsingException.Message);
                CommandLineSwitches.ShowHelp();
                return 1;
            }

            if (CommandLineSwitches.ShouldShowHelp)
            {
                CommandLineSwitches.ShowHelp();
                return 0;
            }

            if (CommandLineSwitches.PrintVersionAndExit)
            {
                Console.WriteLine(ServerVersion.FullVersion);
                return 0;
            }

            new WelcomeMessage(Console.Out).Print();

            var configuration = new RavenConfiguration(null, ResourceType.Server, CommandLineSwitches.CustomConfigPath);

            if (configurationArgs != null)
                configuration.AddCommandLine(configurationArgs);

            configuration.Initialize();

            LoggingSource.Instance.SetupLogMode(configuration.Logs.Mode, configuration.Logs.Path.FullPath);
            if (Logger.IsInfoEnabled)
                Logger.Info($"Logging to { configuration.Logs.Path } set to {configuration.Logs.Mode} level.");

            if (WindowsServiceRunner.ShouldRunAsWindowsService())
            {
                try
                {
                    WindowsServiceRunner.Run(CommandLineSwitches.ServiceName, configuration);
                }
                catch (Exception e)
                {
                    if (Logger.IsInfoEnabled)
                        Logger.Info("Error running Windows Service", e);

                    return 1;
                }

                return 0;
            }

            var rerun = false;
            do
            {
                if (rerun)
                {
                    Console.WriteLine("\nRestarting Server...");
                    rerun = false;
                }

                try
                {
                    using (var server = new RavenServer(configuration))
                    {
                        try
                        {
                            try
                            {
                                server.OpenPipes();
                            }
                            catch (Exception e)
                            {
                                if (Logger.IsInfoEnabled)
                                    Logger.Info("Unable to OpenPipe. Admin Channel will not be available to the user", e);
                                Console.WriteLine("Warning: Admin Channel is not available");
                            }
                            server.Initialize();

                            if (CommandLineSwitches.PrintServerId)
                                Console.WriteLine($"Server ID is {server.ServerStore.GetServerId()}.");

                            new RuntimeSettings(Console.Out).Print();

                            if (CommandLineSwitches.LaunchBrowser)
                                BrowserHelper.OpenStudioInBrowser(server.ServerStore.NodeHttpServerUrl);

                            new ClusterMessage(Console.Out, server.ServerStore).Print();

                            var prevColor = Console.ForegroundColor;
                            Console.Write("Server available on: ");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"{server.ServerStore.NodeHttpServerUrl}");
                            Console.ForegroundColor = prevColor;

                            var tcpServerStatus = server.GetTcpServerStatus();
                            prevColor = Console.ForegroundColor;
                            Console.Write("Tcp listening on ");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"{string.Join(", ", tcpServerStatus.Listeners.Select(l => l.LocalEndpoint))}");
                            Console.ForegroundColor = prevColor;

                            Console.WriteLine("Server started, listening to requests...");

                            IsRunningAsService = false;
                            rerun = CommandLineSwitches.Daemon ? RunAsService() : RunInteractive(server);

                            Console.WriteLine("Starting shut down...");
                            if (Logger.IsInfoEnabled)
                                Logger.Info("Server is shutting down");
                        }
                        catch (Exception e)
                        {
                            if (Logger.IsOperationsEnabled)
                                Logger.Operations("Failed to initialize the server", e);
                            Console.WriteLine(e);

                            return -1;
                        }
                    }

                    Console.WriteLine("Shutdown completed");
                }
                catch (Exception e)
                {
                    if (e.ToString().Contains(@"'WriteReqPool'") &&
                        e.ToString().Contains("System.ObjectDisposedException")) // TODO : Remove this check in dotnet 2.0 - https://github.com/aspnet/KestrelHttpServer/issues/1231
                    {
                        Console.WriteLine("Ignoring Kestrel's Exception : 'Cannot access a disposed object. Object name: 'WriteReqPool'");
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine("Error during shutdown");
                        Console.WriteLine(e);
                        return -2;
                    }
                }
            } while (rerun);

            return 0;
        }

        public static ManualResetEvent ShutdownServerMre = new ManualResetEvent(false);
        public static ManualResetEvent ResetServerMre = new ManualResetEvent(false);

        public static bool IsRunningAsService;

        public static bool RunAsService()
        {
            IsRunningAsService = true;

            if (Logger.IsInfoEnabled)
                Logger.Info("Server is running as a service");
            Console.WriteLine("Running as Service");

            AssemblyLoadContext.Default.Unloading += s =>
            {
                if (ShutdownServerMre.WaitOne(0))
                    return; // already done
                Console.WriteLine("Received graceful exit request...");
                ShutdownServerMre.Set();
            };

            ShutdownServerMre.WaitOne();
            if (ResetServerMre.WaitOne(0))
            {
                ResetServerMre.Reset();
                ShutdownServerMre.Reset();
                return true;
            }
            return false;
        }

        private static bool RunInteractive(RavenServer server)
        {
            var configuration = server.Configuration;

            //stop dumping logs
            LoggingSource.Instance.DisableConsoleLogging();
            LoggingSource.Instance.SetupLogMode(LogMode.None, configuration.Logs.Path.FullPath);

            return new RavenCli().Start(server, Console.Out, Console.In, true);
        }

        public static void WriteServerStatsAndWaitForEsc(RavenServer server)
        {
            Console.WriteLine("Showing stats, press any key to close...");
            Console.WriteLine("    working set     | native mem      | managed mem     | mmap size         | reqs/sec       | docs (all dbs)");
            var i = 0;
            while (Console.KeyAvailable == false)
            {
                var json = MemoryStatsHandler.MemoryStatsInternal();
                var humaneProp = (json["Humane"] as DynamicJsonValue);
                var reqCounter = server.Metrics.RequestsMeter;

                Console.Write($"\r {((i++ % 2) == 0 ? "*" : "+")} ");

                Console.Write($" {humaneProp?["WorkingSet"],-14} ");
                Console.Write($" | {humaneProp?["TotalUnmanagedAllocations"],-14} ");
                Console.Write($" | {humaneProp?["ManagedAllocations"],-14} ");
                Console.Write($" | {humaneProp?["TotalMemoryMapped"],-17} ");

                Console.Write($"| {Math.Round(reqCounter.OneSecondRate, 1),-14:#,#.#;;0} ");

                long allDocs = 0;
                foreach (var value in server.ServerStore.DatabasesLandlord.DatabasesCache.Values)
                {
                    if (value.Status != TaskStatus.RanToCompletion)
                        continue;

                    try
                    {
                        allDocs += value.Result.DocumentsStorage.GetNumberOfDocuments();
                    }
                    catch (Exception)
                    {
                        // may run out of virtual address space, or just shutdown, etc
                    }
                }

                Console.Write($"| {allDocs,14:#,#.#;;0}      ");

                for (int j = 0; j < 5 && Console.KeyAvailable == false; j++)
                {
                    Thread.Sleep(100);
                }
            }

            Console.ReadKey(true);
            Console.WriteLine();
            Console.WriteLine("Stats halted");

        }
    }
}
