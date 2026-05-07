using System;
using System.IO;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class ShellCommand : ICommand
    {
        public string Name => "shell";
        public string Description => "Shell utilities: pwd, cd";

        public string Help => @"[bold]shell[/] - Shell utilities
  [green]pwd[/]          Print working directory
  [green]cd <path>[/]    Change working directory";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "pwd":
                    AnsiConsole.MarkupLine(Markup.Escape(Directory.GetCurrentDirectory()));
                    break;
                case "cd":
                    ExecuteCd(args);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ExecuteCd(string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: shell cd <path>[/]");
                return;
            }

            var path = string.Join(" ", args, 1, args.Length - 1);

            if (!Directory.Exists(path))
            {
                AnsiConsole.MarkupLine($"[red]Directory not found: {Markup.Escape(path)}[/]");
                return;
            }

            Directory.SetCurrentDirectory(path);
            AnsiConsole.MarkupLine(Markup.Escape(Directory.GetCurrentDirectory()));
        }
    }
}
