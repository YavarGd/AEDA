using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Chat;

/// <summary>
/// The shared hidden-reasoning filter used by both Assist and General Chat. The streaming cases
/// matter most: a provider is free to split <c>&lt;think&gt;</c> across chunk boundaries, and the
/// filter must never let the tag or its payload appear even for a single frame.
/// </summary>
public sealed class HiddenReasoningSanitizerTests
{
    [Fact]
    public void CompleteThinkBlock_IsRemoved()
    {
        Assert.Equal(
            "Answer",
            HiddenReasoningSanitizer.SanitizeCompleted("<think>private reasoning</think>Answer"));
    }

    [Fact]
    public void CompleteAnalysisBlock_IsRemoved()
    {
        Assert.Equal(
            "Answer",
            HiddenReasoningSanitizer.SanitizeCompleted("<analysis>private reasoning</analysis>Answer"));
    }

    [Fact]
    public void UnclosedBlock_IsRemovedToEndOfBuffer()
    {
        Assert.Equal(
            "Visible.",
            HiddenReasoningSanitizer.SanitizeCompleted("Visible.<think>never closed"));
        Assert.Equal(
            "Visible.",
            HiddenReasoningSanitizer.SanitizeVisible("Visible.<think>never closed"));
    }

    [Fact]
    public void VisibleAnswerBeforeAndAfterHiddenBlock_IsPreserved()
    {
        Assert.Equal(
            "Before. After.",
            HiddenReasoningSanitizer.SanitizeCompleted("Before. <think>secret</think>After."));
    }

    [Fact]
    public void TagsAreMatchedCaseInsensitively()
    {
        Assert.Equal(
            "Answer",
            HiddenReasoningSanitizer.SanitizeCompleted("<THINK>secret</Think>Answer"));
    }

    /// <summary>
    /// The opening tag arrives in pieces. Every intermediate buffer must be safe, including the
    /// one that holds only "&lt;thi" — that fragment is withheld rather than shown, because it is
    /// about to become a tag.
    /// </summary>
    [Fact]
    public void OpeningTagSplitAcrossChunks_NeverSurfacesTagOrPayload()
    {
        AssertStreamIsSafe(
            ["Answer. ", "<thi", "nk>secret reasoning", "</think>Done."],
            "Answer. Done.");
    }

    [Fact]
    public void ClosingTagSplitAcrossChunks_KeepsPayloadHiddenUntilBlockCloses()
    {
        AssertStreamIsSafe(
            ["<think>secret reasoning", "</thi", "nk>", "Done."],
            "Done.");
    }

    [Fact]
    public void SingleCharacterChunks_NeverSurfaceTagOrPayload()
    {
        var raw = "Before.<analysis>secret reasoning</analysis>After.";
        AssertStreamIsSafe([.. raw.Select(character => character.ToString())], "Before.After.");
    }

    [Fact]
    public void OrdinaryMarkupIsUnaffected()
    {
        const string value = "Use <code>x &lt; y</code> and <xml><node/></xml> here.";

        Assert.Equal(value, HiddenReasoningSanitizer.SanitizeCompleted(value));
        Assert.Equal(value, HiddenReasoningSanitizer.SanitizeVisible(value));
    }

    /// <summary>
    /// A trailing '&lt;t' is withheld only while more chunks may still arrive. Once the response
    /// is complete it cannot become a tag, so it is ordinary text and is kept.
    /// </summary>
    [Fact]
    public void TrailingTagFragment_IsWithheldWhileStreamingAndKeptWhenComplete()
    {
        Assert.Equal("Answer", HiddenReasoningSanitizer.SanitizeVisible("Answer<t"));
        Assert.Equal("Answer<t", HiddenReasoningSanitizer.SanitizeCompleted("Answer<t"));
    }

    [Fact]
    public void NonMatchingFragmentIsNotWithheld()
    {
        Assert.Equal("Answer<x", HiddenReasoningSanitizer.SanitizeVisible("Answer<x"));
    }

    [Fact]
    public void StreamingPreservesWhitespaceSoChunksReadNaturally()
    {
        Assert.Equal("First ", HiddenReasoningSanitizer.SanitizeVisible("First "));
        Assert.Equal("First", HiddenReasoningSanitizer.SanitizeCompleted("First "));
    }

    [Fact]
    public void EmptyInputIsSafe()
    {
        Assert.Equal(string.Empty, HiddenReasoningSanitizer.SanitizeVisible(null));
        Assert.Equal(string.Empty, HiddenReasoningSanitizer.SanitizeCompleted(null));
        Assert.Equal(string.Empty, HiddenReasoningSanitizer.SanitizeCompleted("<think>only reasoning</think>"));
    }

    /// <summary>
    /// Feeds the chunks one at a time and asserts that no intermediate visible projection ever
    /// leaks a tag or the reasoning payload, then checks the completed text.
    /// </summary>
    private static void AssertStreamIsSafe(IReadOnlyList<string> chunks, string expectedCompleted)
    {
        var buffer = new HiddenReasoningBuffer();
        foreach (var chunk in chunks)
        {
            buffer.Append(chunk);
            var visible = buffer.VisibleText;

            Assert.DoesNotContain("think", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("analysis", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('<', visible);
        }

        Assert.Equal(expectedCompleted, buffer.CompletedText);
    }
}
