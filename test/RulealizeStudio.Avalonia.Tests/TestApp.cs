// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RulealizeStudio.Hosting;
using RulealizeStudio.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace RulealizeStudio.Tests;

/// <summary>The builder the headless run starts with: the application every window here is opened in, as RuleApp starts it.</summary>
public static class TestApp
{
    /// <summary>Configures Avalonia for a run with no window server.</summary>
    /// <returns>The builder.</returns>
    /// <remarks>Each test opens its own windows: with no desktop lifetime the application opens none.</remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new RuleApplication(typeof(TestApp).Assembly))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
