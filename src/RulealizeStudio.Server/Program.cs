// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace RulealizeStudio.Server;

/// <summary>The entry point.</summary>
public static class Program
{
    /// <summary>
    /// With nothing on the command line, speaks the language server protocol on standard input and
    /// output until told to exit, and reports what it failed over on standard error. With a check
    /// on it, runs that check in an application's folder, as <see cref="Command"/> says.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            // What is found is said in the rule set's terms, a route's arrows and a part's chevron among them.
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // And what the designer is asked is somebody's words, in their language.
            using StreamReader input = new(Console.OpenStandardInput(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Command.Run(args, input, Console.Out, Console.Error);
        }

        await LanguageServer.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
        return 0;
    }
}
