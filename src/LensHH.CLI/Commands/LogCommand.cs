using System;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class LogCommand : ICommand
    {
        public string Name => "log";
        public string Description => "Log session input and output to a file";

        public string Help => @"[bold]log[/] - Session logging
  [green]log start <path>[/]    Start logging to file (appends if file exists)
  [green]log stop[/]            Stop logging
  [green]log status[/]          Show current logging state";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "start":
                    if (args.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: log start <path>[/]");
                        return;
                    }
                    var path = string.Join(" ", args, 1, args.Length - 1);
                    session.StartLogging(path);
                    AnsiConsole.MarkupLine($"[green]Logging to: {Markup.Escape(path)}[/]");
                    break;

                case "stop":
                    session.StopLogging();
                    AnsiConsole.MarkupLine("[green]Logging stopped.[/]");
                    break;

                case "status":
                    if (session.IsLogging)
                        AnsiConsole.MarkupLine($"[green]Logging active: {Markup.Escape(session.LogFilePath!)}[/]");
                    else
                        AnsiConsole.MarkupLine("[grey]Logging is not active.[/]");
                    break;

                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }
    }
}
