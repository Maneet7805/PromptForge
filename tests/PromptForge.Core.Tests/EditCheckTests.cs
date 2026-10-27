using PromptForge.Core.Diffing;

namespace PromptForge.Core.Tests;

public class EditCheckTests
{
    [Fact]
    public void Identical_text_has_no_edits() =>
        Assert.False(EditCheck.HasEdits("Fix the login bug.", "Fix the login bug."));

    [Fact]
    public void Windows_line_breaks_in_the_box_match_unix_line_breaks_in_the_result() =>
        Assert.False(EditCheck.HasEdits("Line one\r\nLine two\r\n\r\nLine four", "Line one\nLine two\n\nLine four"));

    [Fact]
    public void A_changed_word_is_an_edit() =>
        Assert.True(EditCheck.HasEdits("Fix the login bug.", "Fix the signup bug."));

    [Fact]
    public void A_removed_line_break_is_an_edit() =>
        Assert.True(EditCheck.HasEdits("Line one Line two", "Line one\nLine two"));

    [Fact]
    public void A_trailing_line_break_is_an_edit() =>
        Assert.True(EditCheck.HasEdits("Fix the bug.\r\n", "Fix the bug."));

    [Fact]
    public void Empty_text_on_both_sides_has_no_edits() =>
        Assert.False(EditCheck.HasEdits("", ""));

    [Fact]
    public void Clearing_the_box_is_an_edit() =>
        Assert.True(EditCheck.HasEdits("", "Fix the bug."));

    [Fact]
    public void Unix_line_breaks_become_windows_line_breaks() =>
        Assert.Equal("a\r\nb\r\n\r\nc", EditCheck.ToWindowsNewlines("a\nb\n\nc"));

    [Fact]
    public void Windows_line_breaks_are_not_doubled() =>
        Assert.Equal("a\r\nb", EditCheck.ToWindowsNewlines("a\r\nb"));
}
