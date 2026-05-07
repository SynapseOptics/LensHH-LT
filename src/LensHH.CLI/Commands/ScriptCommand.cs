using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class ScriptCommand : ICommand
    {
        public string Name => "script";
        public string Description => "Run a script file with optional arguments";

        public string Help => @"[bold]script[/] - Run a script file
  [green]script <path> [arg1 arg2 ...][/]   Execute commands from a script file

Script files contain one command per line. Empty lines and lines starting with # are ignored.
Arguments are available as $1, $2, ... and $0 is the script path.
$# expands to the number of arguments.

Example script (design.txt):
  file open $1
  glass load $2
  analysis paraxial
  analysis spot 0

Usage:
  script design.txt ""C:\lenses\doublet.zmx"" ""C:\catalogs""";

        private readonly CommandDispatcher _dispatcher;

        public ScriptCommand(CommandDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            var scriptPath = args[0];
            if (!File.Exists(scriptPath))
            {
                AnsiConsole.MarkupLine($"[red]Script file not found: {Markup.Escape(scriptPath)}[/]");
                return;
            }

            // Build argument list: $0 = script path, $1..$N = arguments
            var scriptArgs = new string[args.Length];
            Array.Copy(args, scriptArgs, args.Length);

            var lines = File.ReadAllLines(scriptPath);
            int lineNum = 0;

            foreach (var rawLine in lines)
            {
                lineNum++;
                var line = rawLine.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                // Substitute arguments
                var expanded = SubstituteArgs(line, scriptArgs);

                AnsiConsole.MarkupLine($"[grey]>> {Markup.Escape(expanded)}[/]");

                try
                {
                    _dispatcher.Dispatch(session, expanded);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Script error at line {lineNum}: {Markup.Escape(ex.Message)}[/]");
                    return;
                }
            }

            AnsiConsole.MarkupLine($"[green]Script completed: {Markup.Escape(scriptPath)}[/]");
        }

        internal static string SubstituteArgs(string line, string[] args)
        {
            // Replace $# with argument count (excluding $0)
            line = line.Replace("$#", (args.Length - 1).ToString());

            // Replace $N with corresponding argument (highest numbers first to avoid $1 matching inside $10)
            for (int i = args.Length - 1; i >= 0; i--)
            {
                line = line.Replace($"${i}", args[i]);
            }

            return line;
        }
    }
}
