using System.Text.Json;
using UniSphere.Api.Services;
using Xunit;

namespace UniSphere.Api.Tests;

/// <summary>Unit coverage for the tolerant LLM-JSON extractor used by the
/// exam and quiz parsers: clean, fenced, reasoning-wrapped, prose-wrapped,
/// bracket-laden-string, and truncated-output shapes must all resolve;
/// genuine garbage must keep failing.</summary>
public class AiJsonExtractorTests
{
    private static JsonDocument Doc(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CleanObject_IsExtracted()
    {
        var ok = AiJsonExtractor.TryExtract(
            "{\"questions\":[{\"questionText\":\"What is an index?\"}]}", out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
        Assert.True(el.TryGetProperty("questions", out _));
    }

    [Fact]
    public void BareArray_IsExtracted()
    {
        var ok = AiJsonExtractor.TryExtract("[{\"question\":\"Q1\"}]", out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Array, el.ValueKind);
        Assert.Equal(1, el.GetArrayLength());
    }

    [Fact]
    public void FencedJson_IsExtracted()
    {
        var ok = AiJsonExtractor.TryExtract(
            "```json\n{\"questions\":[]}\n```", out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
    }

    [Fact]
    public void ReasoningTagWrapped_IsStrippedAndExtracted()
    {
        // Reasoning models may inline a think-tag block before the payload.
        var raw = "<" + "think" + ">" + "The user wants JSON about indexes." + "<" + "/" + "think" + ">"
                  + "{\"questions\":[{\"questionText\":\"Q\"}]}";
        var ok = AiJsonExtractor.TryExtract(raw, out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
        Assert.True(el.TryGetProperty("questions", out _));
    }

    [Fact]
    public void ProseWrapped_IsExtracted()
    {
        var ok = AiJsonExtractor.TryExtract(
            "Here is your exam:\n{\"questions\":[{\"questionText\":\"Q\"}]}\nGood luck!",
            out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
    }

    [Fact]
    public void BracketInsideStringLiteral_DoesNotBreakExtraction()
    {
        // The old parser sliced from the FIRST '[' — which sat inside a string
        // value — and produced invalid JSON. The balanced scan must not.
        var ok = AiJsonExtractor.TryExtract(
            "{\"questions\":[{\"questionText\":\"Pick A] now\",\"optionsJson\":[\"A]\",\"B[\"]}]}",
            out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
    }

    [Fact]
    public void TruncatedArray_SalvagesCompleteQuestions()
    {
        // Simulates a max-tokens cut mid-array: two complete objects then a cut.
        var raw = "{\"questions\":[{\"questionText\":\"Q1\"},{\"questionText\":\"Q2\"},{\"questionText\":\"Q3";
        var ok = AiJsonExtractor.TryExtract(raw, out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Array, el.ValueKind);   // salvaged as an array of objects
        Assert.Equal(2, el.GetArrayLength());
    }

    [Fact]
    public void Garbage_FailsFast()
    {
        Assert.False(AiJsonExtractor.TryExtract("no json here at all", out _));
        Assert.False(AiJsonExtractor.TryExtract("", out _));
        Assert.False(AiJsonExtractor.TryExtract("{\"a\":", out _));
    }

    [Fact]
    public void NestedSameType_Openers_PairAtTheRightClose()
    {
        var ok = AiJsonExtractor.TryExtract(
            "{\"a\":{\"b\":{\"c\":1,\"d\":[2,3]}},\"questions\":[]}", out var el);
        Assert.True(ok);
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
        Assert.True(el.TryGetProperty("questions", out _));
    }
}
