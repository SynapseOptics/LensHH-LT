using System;
using System.Collections.Generic;
using System.Linq;
using LensHH.CLI.Commands;
using Spectre.Console;

namespace LensHH.CLI
{
    public class CommandDispatcher
    {
        private readonly Dictionary<string, ICommand> _commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);

        public void Register(ICommand command)
        {
            _commands[command.Name] = command;
        }

        public void Dispatch(Session session, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return;

            // Log the input command if logging is active
            session.LogInput(input);

            var parts = ParseInput(input);
            if (parts.Length == 0)
                return;

            var commandName = parts[0];
            var args = parts.Skip(1).ToArray();

            if (commandName.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length > 0 && _commands.TryGetValue(args[0], out var helpCmd))
                {
                    AnsiConsole.MarkupLine(helpCmd.Help);
                }
                else
                {
                    ShowHelp();
                }
                return;
            }

            // Shortcut: pwd and cd are top-level aliases for shell pwd / shell cd
            if (commandName.Equals("pwd", StringComparison.OrdinalIgnoreCase) ||
                commandName.Equals("cd", StringComparison.OrdinalIgnoreCase))
            {
                if (_commands.TryGetValue("shell", out var shellCmd))
                {
                    try
                    {
                        var shellArgs = new string[args.Length + 1];
                        shellArgs[0] = commandName;
                        Array.Copy(args, 0, shellArgs, 1, args.Length);
                        shellCmd.Execute(session, shellArgs);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                    }
                }
                return;
            }

            if (_commands.TryGetValue(commandName, out var command))
            {
                try
                {
                    command.Execute(session, args);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Unknown command: {Markup.Escape(commandName)}. Type 'help' for available commands.[/]");
            }
        }

        private void ShowHelp()
        {
            var table = new Table();
            table.AddColumn("Command");
            table.AddColumn("Description");

            foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
            {
                table.AddRow(Markup.Escape(cmd.Name), Markup.Escape(cmd.Description));
            }

            // Built-ins handled outside the ICommand registry — surface
            // them in the help table so first-time users can find them
            // without having to guess.
            table.AddRow("help", "Show this list, or 'help <command>' for command-specific usage");
            table.AddRow("quit / exit", "Exit the CLI");

            AnsiConsole.Write(table);
        }

        private static string[] ParseInput(string input)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            foreach (char c in input)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                parts.Add(current.ToString());

            return parts.ToArray();
        }
    }
}
