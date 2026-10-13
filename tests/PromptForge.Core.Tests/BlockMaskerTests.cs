using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

public class BlockMaskerTests
{
    [Fact]
    public void Short_text_is_left_alone()
    {
        var result = BlockMasker.Mask("explain closures in javascript");
        Assert.False(result.HasBlocks);
        Assert.Equal("explain closures in javascript", result.Text);
    }

    [Fact]
    public void Large_fenced_block_is_masked_and_restored_verbatim()
    {
        var code = "```python\ndef add(a, b):\n    return a + b\n\nprint(add(1, 2))\n```";
        var draft = $"why does this print the wrong thing\n\n{code}\n\nthanks";

        var masked = BlockMasker.Mask(draft);

        Assert.Single(masked.Blocks);
        Assert.Contains("[[BLOCK_1]]", masked.Text);
        Assert.DoesNotContain("def add", masked.Text);
        Assert.Contains("code block (python)", masked.Blocks[0].Description);

        var rewrite = "This Python code prints the wrong result. Explain why.\n\n[[BLOCK_1]]";
        var restored = BlockMasker.Unmask(rewrite, masked.Blocks);
        Assert.Empty(restored.MissingMarkers);
        Assert.Contains(code, restored.Text);
    }

    [Fact]
    public void Small_fenced_snippet_stays_visible_to_the_model()
    {
        var masked = BlockMasker.Mask("rename ```x``` to ```y```");
        Assert.False(masked.HasBlocks);
    }

    [Fact]
    public void Stack_trace_without_fences_is_detected()
    {
        var trace = string.Join("\n",
            "Traceback (most recent call last):",
            "  File \"app.py\", line 10, in <module>",
            "    main()",
            "  File \"app.py\", line 6, in main",
            "    result = compute(data)",
            "  File \"lib.py\", line 22, in compute",
            "    return data['value'] / count",
            "ZeroDivisionError: division by zero");
        var draft = $"getting this error when i run my script, how do i fix it\n{trace}";

        var masked = BlockMasker.Mask(draft);

        Assert.Single(masked.Blocks);
        Assert.StartsWith("getting this error", masked.Text);
        Assert.Contains("ZeroDivisionError", masked.Blocks[0].Content);
    }

    [Fact]
    public void Ordinary_multi_paragraph_prose_is_not_masked()
    {
        var draft = string.Join("\n",
            "I want to plan my final year project and I am not sure where to start with it.",
            "The project should involve machine learning because that is what I enjoy most.",
            "My supervisor said it needs a clear research question and a realistic scope too.",
            "I have about six months and I can spend around ten hours every single week on it.",
            "Please help me think about possible topics and how I would break down the work.",
            "I would also like advice on how to keep a log of progress for the final report.");
        Assert.False(BlockMasker.Mask(draft).HasBlocks);
    }

    [Fact]
    public void Tagged_document_is_masked()
    {
        var doc = "<document>" + string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 20)) + "</document>";
        var masked = BlockMasker.Mask("summarise this for me " + doc);
        Assert.Single(masked.Blocks);
        Assert.Contains("<document> section", masked.Blocks[0].Description);
    }

    [Fact]
    public void Dropped_marker_appends_material_and_duplicates_are_removed()
    {
        var blocks = new[]
        {
            new MaskedBlock("[[BLOCK_1]]", "AAA", "x"),
            new MaskedBlock("[[BLOCK_2]]", "BBB", "y"),
        };
        var restored = BlockMasker.Unmask("Intro\n[[BLOCK_1]]\nagain [[BLOCK_1]]", blocks);

        Assert.Equal(["[[BLOCK_2]]"], restored.MissingMarkers);
        Assert.Equal(1, CountOf(restored.Text, "AAA"));
        Assert.EndsWith("BBB", restored.Text);
        Assert.DoesNotContain("[[BLOCK_", restored.Text);
    }

    [Fact]
    public void Windows_line_endings_are_normalised()
    {
        var masked = BlockMasker.Mask("hello\r\nworld");
        Assert.Equal("hello\nworld", masked.Text);
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, "").Length) / value.Length;
}
