// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using RulealizeStudio.Binding;

namespace RulealizeStudio.Tests;

/// <summary>A label document: the host's sentences for what a rule set gives only a code.</summary>
public class LabelsTests
{
    private const string Signup = """
        {
          "$schema": "rulealize-studio/labels/v1",
          // A comment, as a rule set may have.
          "labels": {
            "/inputs/setName/validate/name.reserved": "Nobody books under that name."
          }
        }
        """;

    [Fact]
    public void ARefusalIsSaidInItsSentenceAndOneWithNoneByItsCode()
    {
        Labels labels = Labels.Read("en", Signup);

        Assert.Equal("Nobody books under that name.", labels.Say("setName", "name.reserved"));
        Assert.Equal("name.unchanged", labels.Say("setName", "name.unchanged"));
        Assert.Equal("name.reserved", Labels.Say(null, "setName", "name.reserved"));
        Assert.Equal("/inputs/setName/validate/name.reserved", Assert.Single(labels.Sentences).Key);
    }

    [Fact]
    public void ALabelIsKeyedByTheNameItsClauseHasInASpecification()
    {
        Assert.Equal("/inputs/setName/validate/name.reserved", Labels.Refusal("setName", "name.reserved"));
        Assert.Equal("/inputs/a~1b/validate/c~0d", Labels.Refusal("a/b", "c~d"));
    }

    [Theory]
    [InlineData("""{ "labels": {} }""")]
    [InlineData("""{ "$schema": "rulealize-studio/labels/v1", "labels": { "/inputs/setName": "Your name" } }""")]
    [InlineData("""{ "$schema": "rulealize-studio/labels/v1", "labels": { "/state/schema/name": "Name" } }""")]
    [InlineData("""{ "$schema": "rulealize-studio/labels/v1", "labels": { "/inputs/setName/validate/name.reserved": 3 } }""")]
    [InlineData("not JSON")]
    public void WhatIsNotALabelForARefusalIsRefused(string text)
    {
        Assert.Throws<FormatException>(() => Labels.Read("en", text));
    }

    [Fact]
    public void ALabelDocumentIsFoundByItsRuleSetsNameAndALanguage()
    {
        Assert.Equal("en", Labels.LanguageOf("signup.json", @"C:\app\signup.labels.en.json"));
        Assert.Equal("en-GB", Labels.LanguageOf("signup.json", "signup.labels.en-GB.json"));
        Assert.Null(Labels.LanguageOf("signup.json", "signup.json"));
        Assert.Null(Labels.LanguageOf("signup.json", "signup.labels..json"));
        Assert.Null(Labels.LanguageOf("signup.json", "countdown.labels.en.json"));
    }

    [Fact]
    public void ThePersonsLanguageIsSpokenOrTheNearestOrElseTheFirst()
    {
        Labels[] written = [Labels.Read("ja", Signup), Labels.Read("en", Signup), Labels.Read("en-GB", Signup)];

        Assert.Equal("en-GB", Labels.Choose(written, CultureInfo.GetCultureInfo("en-GB"))?.Language);
        Assert.Equal("en", Labels.Choose(written, CultureInfo.GetCultureInfo("en-US"))?.Language);
        Assert.Equal("ja", Labels.Choose(written, CultureInfo.GetCultureInfo("ja-JP"))?.Language);
        Assert.Equal("en", Labels.Choose(written, CultureInfo.GetCultureInfo("fr-FR"))?.Language);
        Assert.Null(Labels.Choose([], CultureInfo.GetCultureInfo("en-US")));
    }
}
