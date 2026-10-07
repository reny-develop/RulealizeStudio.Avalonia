// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace RulealizeStudio.Generator;

/// <summary>The model a window's XAML binds: its root's <c>x:DataType</c>, as a type's full name.</summary>
/// <remarks>
/// <para>
/// Compiling a window's XAML keeps which file it was and drops its <c>x:DataType</c>, so the
/// application could not tell at run time which rule set's model a window is to be opened on. It is
/// read here, at build time, and written into the assembly beside the file's name, as
/// <see cref="Prefix"/> and the file in an <c>AssemblyMetadata</c>; <c>RuleWindows</c> in the hosting
/// library reads it back.
/// </para>
/// <para>
/// Nothing else of the XAML is read: not a control, not a binding.
/// </para>
/// </remarks>
internal static class Window
{
    /// <summary>What an <c>AssemblyMetadata</c> key starts with before the file a window is written in.</summary>
    public const string Prefix = "RulealizeStudio.Window:";

    /// <summary>The <c>AssemblyMetadata</c> key that says the windows were recorded, whether or not any binds a model.</summary>
    public const string Recorded = "RulealizeStudio.Windows";

    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Reads the type a window's XAML binds.</summary>
    /// <param name="text">The XAML.</param>
    /// <returns>The type's full name; null where the root is no window, or binds nothing, or the text is not XML.</returns>
    public static string? Bound(string text)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(text).Root;
        }
        catch (XmlException)
        {
            return null;
        }

        if (root is null || root.Name.LocalName != "Window" || root.Attribute(XName.Get("DataType", Xaml))?.Value is not { } named)
        {
            return null;
        }

        int colon = named.IndexOf(':');
        string space = colon < 0 ? string.Empty : root.GetNamespaceOfPrefix(named.Substring(0, colon))?.NamespaceName ?? string.Empty;
        string name = colon < 0 ? named : named.Substring(colon + 1);
        foreach (string prefix in new[] { "using:", "clr-namespace:" })
        {
            if (space.StartsWith(prefix, StringComparison.Ordinal))
            {
                space = space.Substring(prefix.Length).Split(';')[0];
            }
        }

        return space.Length == 0 || space.Contains("://") ? name : $"{space}.{name}";
    }

    /// <summary>A file's path from the project's folder, with forward slashes, as Avalonia names a compiled window.</summary>
    /// <param name="file">The file's full path.</param>
    /// <param name="project">The project's folder, or empty where it is not known.</param>
    /// <returns>The path, or the file's name alone where it is not under the project's folder.</returns>
    public static string Relative(string file, string project)
    {
        string full = file.Replace('\\', '/');
        string folder = project.Replace('\\', '/').TrimEnd('/') + "/";
        return project.Length > 0 && full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(folder.Length)
            : Path.GetFileName(file);
    }
}
