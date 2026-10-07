// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;

namespace RulealizeStudio.Generator;

/// <summary>How a name the rule set chose becomes a name C# accepts.</summary>
internal static class Name
{
    /// <summary>The name with each word capitalised and anything C# refuses dropped.</summary>
    public static string Pascal(string name)
    {
        StringBuilder built = new();
        bool upper = true;

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            built.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        if (built.Length == 0 || char.IsDigit(built[0]))
        {
            built.Insert(0, '_');
        }

        return built.ToString();
    }
}
