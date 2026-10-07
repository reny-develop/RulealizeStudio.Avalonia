// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Hosting;

/// <summary>Every window of an application, each on the model of the rule set it binds, shown while the rules say.</summary>
/// <remarks>
/// <para>
/// An application is as many windows and rule sets as it needs, none counted against the other. A
/// window is any XAML in the application's assembly whose root is one; it binds the rule set whose
/// model its <c>x:DataType</c> names — which compiling the XAML drops, so the generator writes it
/// into the assembly beside the file's name — and every window binding the same one is given the
/// same model, so they stand in the same place. A window with no <c>x:DataType</c> — a folder with no
/// rules yet — is given nothing.
/// </para>
/// <para>
/// When each is shown and what its × does are <see cref="RuleWindow"/>'s, kept here: a window is
/// shown and hidden as its <see cref="RuleWindow.ShowWhenProperty"/> changes, its × makes the move
/// <see cref="RuleWindow.CloseWithProperty"/> names and closes nothing itself, and one bound to a
/// rule set with no such move is not closed by its × at all. When none is shown,
/// <see cref="Emptied"/> is raised, and the application ends.
/// </para>
/// </remarks>
public sealed class RuleWindows
{
    /// <summary>What the generator starts the key of the <c>AssemblyMetadata</c> it writes for a window with, before its file.</summary>
    private const string Prefix = "RulealizeStudio.Window:";

    /// <summary>The key of the <c>AssemblyMetadata</c> the generator writes once it has recorded the windows at all.</summary>
    private const string Recorded = "RulealizeStudio.Windows";

    private readonly List<(string Name, Window Window)> _windows;
    private readonly IReadOnlyList<RuleModel> _models;
    private bool _ending;

    /// <summary>Whether <see cref="Show"/> was called: before it, the rules show nothing.</summary>
    private bool _started;

    private RuleWindows(List<(string Name, Window Window)> windows, IReadOnlyList<RuleModel> models)
    {
        _windows = windows;
        _models = models;
    }

    /// <summary>Raised when no window is shown any more.</summary>
    public event EventHandler? Emptied;

    /// <summary>Gets every window, by the file it is written in, in the order of their names.</summary>
    public IReadOnlyList<Window> Windows => [.. _windows.Select(each => each.Window)];

    /// <summary>Gets the model of each rule set the application was built from.</summary>
    public IReadOnlyList<RuleModel> Models => _models;

    /// <summary>Gets every window shown now.</summary>
    public IReadOnlyList<Window> Shown => [.. _windows.Select(each => each.Window).Where(window => window.IsVisible)];

    /// <summary>Opens every window of an application, each on its rule set's model, none shown yet.</summary>
    /// <param name="application">The application's assembly, which holds its windows and the models generated from its rule sets.</param>
    /// <returns>The windows.</returns>
    /// <exception cref="InvalidOperationException">A window binds a type that is no model of the assembly's, or the assembly holds no window.</exception>
    /// <remarks>What opening a rule set throws is thrown as it is.</remarks>
    public static RuleWindows Open(Assembly application)
    {
        ArgumentNullException.ThrowIfNull(application);

        Dictionary<Type, RuleModel> models = [];
        List<(string Name, Window Window)> windows = [];
        string assembly = application.GetName().Name!;

        // Built before the generator recorded which model a window binds, an application was one
        // window over one rule set, and the window is given that one.
        IReadOnlyList<Type> types = RuleApp.Models(application);
        Type? only = !application.GetCustomAttributes<AssemblyMetadataAttribute>().Any(each => each.Key == Recorded) && types.Count == 1
            ? types[0]
            : null;

        foreach (string file in Files(application))
        {
            if (AvaloniaXamlLoader.Load(new Uri($"avares://{assembly}/{file}")) is not Window window)
            {
                continue;
            }

            if ((Bound(application, file) ?? only) is { } bound)
            {
                if (!models.TryGetValue(bound, out RuleModel? model))
                {
                    model = Make(bound);
                    models[bound] = model;
                }

                window.DataContext = model;
            }

            windows.Add((file, window));
        }

        if (windows.Count == 0)
        {
            throw new InvalidOperationException($"'{assembly}' holds no window: no XAML in it is one.");
        }

        RuleWindows screens = new(windows, [.. types.Select(type => models.TryGetValue(type, out RuleModel? made) ? made : Make(type))]);
        foreach ((_, Window window) in windows)
        {
            screens.Keep(window);
        }

        return screens;
    }

    /// <summary>Gets the window written in a file.</summary>
    /// <param name="file">The file, from the application's folder: <c>MainWindow.axaml</c>.</param>
    /// <returns>The window, or null where no window is written there.</returns>
    public Window? Window(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        string asked = file.Replace('\\', '/');
        return _windows.FirstOrDefault(each => string.Equals(each.Name, asked, StringComparison.OrdinalIgnoreCase)).Window;
    }

    /// <summary>Gets the file a window is written in, from the application's folder.</summary>
    /// <param name="window">One of the windows.</param>
    /// <returns>The file, or null where the window is none of these.</returns>
    public string? File(Window window) => _windows.FirstOrDefault(each => ReferenceEquals(each.Window, window)).Name;

    /// <summary>Gets the model of a rule set.</summary>
    /// <param name="ruleSet">The rule set, by its id, with its version after an @ or without.</param>
    /// <returns>Its model, or null where the application was built from no rule set of that id.</returns>
    public RuleModel? Model(string ruleSet) => _models.FirstOrDefault(model => model.RuleSet == ruleSet || model.RuleSet.StartsWith(ruleSet + "@", StringComparison.Ordinal));

    /// <summary>Gets every window bound to a model, shown or not.</summary>
    /// <param name="model">One of the models.</param>
    /// <returns>The windows, in the order of their files.</returns>
    public IReadOnlyList<Window> Of(RuleModel model) => [.. Windows.Where(window => ReferenceEquals(window.DataContext, model))];

    /// <summary>Shows every window the rules have shown, and every one shown from the start.</summary>
    /// <remarks>Called once, after the application has started.</remarks>
    public void Show()
    {
        _started = true;
        foreach (Window window in Windows.Where(window => !RuleWindow.IsShownByRules(window) || RuleWindow.GetShowWhen(window)))
        {
            window.Show();
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Closes every window, whatever the rules say: the application is ending.</summary>
    public void End()
    {
        _ending = true;
        foreach (Window window in Windows)
        {
            window.Close();
        }
    }

    /// <summary>Every file of the assembly's whose XAML was compiled into a window, from the application's folder, in order.</summary>
    /// <remarks>
    /// Avalonia compiles each XAML file into a method that builds it, named after the file, on one
    /// class of the assembly's; a window's is the one that answers a window.
    /// </remarks>
    private static IEnumerable<string> Files(Assembly application)
    {
        const string Build = "Build:/";
        Type? compiled = application.GetType("CompiledAvaloniaXaml.!AvaloniaResources");

        return compiled is null
            ? []
            : compiled.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name.StartsWith(Build, StringComparison.Ordinal) && typeof(Window).IsAssignableFrom(method.ReturnType))
                .Select(method => method.Name[Build.Length..])
                .Order(StringComparer.Ordinal);
    }

    /// <summary>The model a window binds, as its <c>x:DataType</c> named it and the generator wrote it into the assembly; null where it binds none.</summary>
    /// <exception cref="InvalidOperationException">It binds a type that is no model of the assembly's.</exception>
    private static Type? Bound(Assembly application, string file)
    {
        string key = Prefix + file;
        if (application.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(each => each.Key == key)?.Value is not { } named)
        {
            return null;
        }

        Type bound = application.GetType(named)
            ?? throw new InvalidOperationException($"'{file}' binds {named}, which '{application.GetName().Name}' has no type of.");
        return bound.IsSubclassOf(typeof(RuleModel))
            ? bound
            : throw new InvalidOperationException($"'{file}' binds {named}, which is not a model generated from a rule set.");
    }

    private static RuleModel Make(Type model) =>
        (RuleModel)(model.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException($"{model.Name} cannot be made with nothing, as a generated model is."))
        .Invoke(BindingFlags.DoNotWrapExceptions, binder: null, parameters: [], culture: null);

    /// <summary>Shows and hides a window as the rules say, and makes its × the move it is bound to.</summary>
    private void Keep(Window window)
    {
        window.GetObservable(RuleWindow.ShowWhenProperty).Subscribe(new Watch(shown =>
        {
            if (_ending || !RuleWindow.IsShownByRules(window) || window.IsVisible == shown || !_started)
            {
                return;
            }

            if (shown)
            {
                window.Show();
            }
            else
            {
                window.Hide();
                Empty();
            }
        }));

        window.Closing += (_, closing) =>
        {
            if (_ending || closing.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            {
                return;
            }

            // A window with no rules behind it has nothing to walk, and closes as any does.
            if (window.DataContext is not RuleModel)
            {
                return;
            }

            closing.Cancel = true;
            RuleWindow.Close(window);
        };

        window.Closed += (_, _) => Empty();
    }

    private void Empty()
    {
        if (!_ending && _started && Shown.Count == 0)
        {
            Emptied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>An observer of one property, called with each value it takes.</summary>
    private sealed class Watch(Action<bool> changed) : IObserver<bool>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(bool value) => changed(value);
    }
}
