// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Hosting;

/// <summary>An application made of rule sets and XAML, started from its entry point.</summary>
/// <remarks>
/// <para>
/// The whole of an application's C#, the same in every application:
/// </para>
/// <code>
/// public static class Program
/// {
///     [STAThread]
///     public static int Main(string[] args) => RuleApp.Run(typeof(Program).Assembly, args);
///
///     public static AppBuilder BuildAvaloniaApp() => RuleApp.Build(typeof(Program).Assembly);
/// }
/// </code>
/// <para>
/// The windows are every XAML in the application's assembly whose root is a window, each opened on
/// the model generated from the rule set its <c>x:DataType</c> names, and shown while the rules say
/// (<see cref="RuleWindows"/>). None is named here, so an application with no rule set yet builds and
/// opens its window with nothing bound, and the entry point is not edited when one is written, nor
/// when a window or a rule set is added. <c>BuildAvaloniaApp</c> is there because the Previewer looks
/// for it by that name.
/// </para>
/// </remarks>
public static class RuleApp
{
    /// <summary>The file the window of a new application is written in, beside the entry point.</summary>
    public const string Screen = "MainWindow.axaml";

    /// <summary>Starts the application.</summary>
    /// <param name="application">The application's assembly, which holds its windows and the models generated from its rule sets.</param>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static int Run(Assembly application, string[] args) =>
        Build(application).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

    /// <summary>Configures Avalonia for the application. Named by convention, and by the Previewer.</summary>
    /// <param name="application">The application's assembly, which holds its windows and the models generated from its rule sets.</param>
    /// <returns>The builder.</returns>
    public static AppBuilder Build(Assembly application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return AppBuilder.Configure(() => new RuleApplication(application))
            .UsePlatformDetect()
            .LogToTrace();
    }

    /// <summary>The model of each rule set an application was built from.</summary>
    /// <param name="application">The application's assembly.</param>
    /// <returns>Each type of model, in the order of their names; none where it has no rule set yet.</returns>
    /// <remarks>
    /// A model is any type of the assembly's that is a <see cref="RuleModel"/> and can be made
    /// with nothing, as a generated one opens against the folder it is in.
    /// </remarks>
    public static IReadOnlyList<Type> Models(Assembly application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return [.. application.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(RuleModel)) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
    }
}

/// <summary>The Avalonia application <see cref="RuleApp"/> starts.</summary>
/// <remarks>
/// Opening a rule set can fail — a vocabulary missing from beside the application, a document
/// that does not compile against the ones there — and then a window says why rather than the
/// application failing to start.
/// </remarks>
public sealed class RuleApplication : Application
{
    private readonly Assembly _application;

    /// <summary>Initializes a new instance of the <see cref="RuleApplication"/> class.</summary>
    /// <param name="application">The assembly the windows were compiled into.</param>
    public RuleApplication(Assembly application)
    {
        ArgumentNullException.ThrowIfNull(application);

        _application = application;
    }

    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        RuleWindows screens;
        try
        {
            screens = RuleWindows.Open(_application);
        }
        catch (Exception trouble) when (trouble is Rulealize.PluginLoadException
            or Rulealize.Abstraction.RuleSetBuildException or Rulealize.RuleDocumentException or InvalidOperationException)
        {
            Window said = new()
            {
                Title = _application.GetName().Name,
                Width = 560,
                Height = 240,
                Content = new SelectableTextBlock
                {
                    Text = trouble.Message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(16),
                },
            };
            said.Closed += (_, _) => desktop.Shutdown();
            desktop.MainWindow = said;
            said.Show();
            return;
        }

        screens.Emptied += (_, _) => desktop.Shutdown();
        desktop.ShutdownRequested += (_, _) => screens.End();
        screens.Show();
    }
}
