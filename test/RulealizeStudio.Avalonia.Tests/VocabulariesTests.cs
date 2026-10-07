// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using System.Reflection.Emit;
using RulealizeStudio.Server;

namespace RulealizeStudio.Tests;

/// <summary>An application's build output swept for vocabularies without its files being held, so that it can be built again meanwhile.</summary>
public sealed class VocabulariesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rulealize-vocabularies-").FullName;

    public VocabulariesTests()
    {
        foreach (string file in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            File.Copy(file, Path.Combine(_folder, Path.GetFileName(file)));
        }

        // An assembly nothing in this process has loaded — as an application's own is, to the
        // language server — which a sweep loading from paths would hold.
        PersistedAssemblyBuilder built = new(new AssemblyName(Unloaded), typeof(object).Assembly);
        built.DefineDynamicModule(Unloaded).DefineType("Probe", TypeAttributes.Public).CreateType();
        built.Save(Path.Combine(_folder, Unloaded + ".dll"));
    }

    private static string Unloaded { get; } = "Probe" + Guid.NewGuid().ToString("N");

    [Fact]
    public void ARuleSetCompilesAgainstTheVocabulariesAndNoFileOfTheFolderIsHeld()
    {
        string countdown = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ruleset", "countdown.json"));

        Assert.Empty(Check.Run(countdown, Vocabularies.Load(_folder)));

        // Built again: every file written anew — the one nothing had loaded among them — which a file
        // loaded from its path would refuse.
        foreach (string file in Directory.GetFiles(_folder, "*.dll"))
        {
            File.WriteAllBytes(file, File.ReadAllBytes(file));
        }

        Directory.Delete(_folder, recursive: true);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void TheStampChangesWhenTheFolderIsBuiltAgain()
    {
        string before = Vocabularies.Stamp(_folder);
        string file = Directory.GetFiles(_folder, "*.dll")[0];

        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(1));

        Assert.NotEqual(before, Vocabularies.Stamp(_folder));
        Assert.Equal(string.Empty, Vocabularies.Stamp(Path.Combine(_folder, "nothing")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
