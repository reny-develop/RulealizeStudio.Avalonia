// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Avalonia;
using RulealizeStudio.Hosting;

namespace RulealizeStudio.Sample.Signup;

/// <summary>The entry point, and the only C# this application has.</summary>
public static class Program
{
    /// <summary>Starts the application.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args) => RuleApp.Run(typeof(Program).Assembly, args);

    /// <summary>Configures Avalonia. Named by convention, and by the Previewer.</summary>
    /// <returns>The builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => RuleApp.Build(typeof(Program).Assembly);
}
