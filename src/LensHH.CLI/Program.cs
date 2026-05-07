using System;
using LensHH.CLI.Commands;
using Spectre.Console;

namespace LensHH.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            var dispatcher = new CommandDispatcher();
            dispatcher.Register(new FileCommand());
            dispatcher.Register(new SystemCommand());
            dispatcher.Register(new SurfaceCommand());
            dispatcher.Register(new AnalysisCommand());
            dispatcher.Register(new GlassCommand());
            dispatcher.Register(new MeritCommand());
            dispatcher.Register(new OptimizeCommand());
            dispatcher.Register(new VariableCommand());

            dispatcher.Register(new PickupCommand());
            dispatcher.Register(new ShellCommand());
            dispatcher.Register(new LogCommand());
            dispatcher.Register(new ScriptCommand(dispatcher));
            dispatcher.Register(new LicenseCommand());

            var session = new Session();

            // Try loading existing activation token
            LensHH.Core.Activation.ActivationManager.TryLoadExistingActivation();

            // If command-line arguments provided, run as script
            if (args.Length > 0)
            {
                var scriptCmd = new ScriptCommand(dispatcher);
                scriptCmd.Execute(session, args);
                return;
            }

            // Interactive REPL
            AnsiConsole.MarkupLine("[bold blue]LensHH-LT[/] - Optical Lens Design Tool");
            AnsiConsole.MarkupLine("Type [green]help[/] for available commands, [green]quit[/] to exit.\n");

            while (true)
            {
                AnsiConsole.Markup("[bold]> [/]");
                var input = Console.ReadLine();

                if (input == null)
                    break;

                input = input.Trim();

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                dispatcher.Dispatch(session, input);
                Console.WriteLine();
            }

            session.StopLogging();
            AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
        }
    }
}
