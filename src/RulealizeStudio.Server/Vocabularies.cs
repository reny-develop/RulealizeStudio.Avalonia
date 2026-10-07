// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using System.Runtime.Loader;
using Rulealize;

namespace RulealizeStudio.Server;

/// <summary>The vocabularies in a folder — an application's build output — loaded without holding its files.</summary>
/// <remarks>
/// <para>
/// <see cref="RuleRuntime.LoadPluginsFrom(string)"/> loads every assembly in the folder from its
/// path, and Windows holds a file loaded from its path until the process ends. The language server
/// runs for as long as VS Code is open, so the application's own assembly and every vocabulary
/// beside it stayed held, and the application could not be built again — by its person, or by
/// their agent — until the window was reloaded.
/// </para>
/// <para>
/// Each assembly is read into memory instead, into a context of its own that is let go of once
/// nothing uses what was loaded, and handed to <see cref="RuleRuntime.LoadPlugins(Assembly)"/>. What
/// this process already holds — Rulealize, Avalonia — is the one used, so that a vocabulary is a
/// plugin of the runtime this runs. Whatever is not an assembly, or holds no plugin this runtime can
/// load, is passed over, as a folder swept by the runtime itself is.
/// </para>
/// </remarks>
public static class Vocabularies
{
    /// <summary>A runtime with every plugin the assemblies in a folder hold.</summary>
    /// <param name="folder">The folder: an application's build output, or the folder <c>rulealize restore</c> fills.</param>
    /// <returns>The runtime.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder is not there.</exception>
    public static RuleRuntime Load(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"'{folder}' is not there.");
        }

        RuleRuntime runtime = new();
        Context context = new(folder);
        foreach (string file in Files(folder))
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(file));
            }
            catch (Exception wrong) when (wrong is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                continue;
            }

            try
            {
                runtime.LoadPlugins(assembly);
            }
            catch (Exception wrong) when (wrong is ReflectionTypeLoadException or TypeLoadException or FileLoadException
                or FileNotFoundException or BadImageFormatException or PluginLoadException)
            {
                // Not a vocabulary this runtime can load: the folder is swept, and it is passed over.
            }
        }

        return runtime;
    }

    /// <summary>What the assemblies in a folder are now — each one's name, size and when it was written — which changes when it is built again.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The stamp; empty where the folder is not there.</returns>
    public static string Stamp(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return Directory.Exists(folder)
            ? string.Join('|', Files(folder).Select(file =>
            {
                FileInfo info = new(file);
                return $"{Path.GetRelativePath(folder, file)}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            }))
            : string.Empty;
    }

    /// <summary>The assemblies of a folder, searched one level deep as the runtime searches one: those in it, and those in each folder in it — where <c>rulealize restore</c> puts each vocabulary.</summary>
    private static IEnumerable<string> Files(string folder) =>
        Directory.GetFiles(folder, "*.dll").Order(StringComparer.OrdinalIgnoreCase)
            .Concat(Directory.GetDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)
                .SelectMany(inner => Directory.GetFiles(inner, "*.dll").Order(StringComparer.OrdinalIgnoreCase)));

    /// <summary>Reads an assembly into memory and loads it, so that its file is not held.</summary>
    /// <param name="context">The context it is loaded into.</param>
    /// <param name="file">The assembly's file.</param>
    /// <returns>The assembly.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Assembly Read(AssemblyLoadContext context, string file)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(file);
        using MemoryStream bytes = new(File.ReadAllBytes(file));
        return context.LoadFromStream(bytes);
    }

    /// <summary>Where a folder's assemblies are loaded: from memory, each beside the folder's others, and what this process holds already shared.</summary>
    private sealed class Context(string folder) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (Default.Assemblies.FirstOrDefault(loaded => string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)) is { } held)
            {
                return held;
            }

            string? file = Files(folder).FirstOrDefault(found => string.Equals(Path.GetFileNameWithoutExtension(found), assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            return file is null ? null : Read(this, file);
        }
    }
}
