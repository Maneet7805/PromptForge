using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

public class PreservationCheckerTests
{
    [Theory]
    [InlineData("see https://example.com/docs?a=1 for details", "https://example.com/docs?a=1")]
    [InlineData(@"the file is at C:\Users\me\project\app.config", @"C:\Users\me\project\app.config")]
    [InlineData("edit src/components/Header.tsx please", "src/components/Header.tsx")]
    [InlineData("my Program.cs throws", "Program.cs")]
    [InlineData("call `npm run build` first", "npm run build")]
    [InlineData("the useEffect hook runs twice", "useEffect")]
    [InlineData("rename user_id to account_id", "user_id")]
    [InlineData("upgrade to Python 3.11 please", "3.11")]
    [InlineData("must be under 500 words", "500")]
    [InlineData("the title should be \"Quarterly Review\"", "Quarterly Review")]
    [InlineData("email me at a.b@uni.edu.my", "a.b@uni.edu.my")]
    public void Extracts_exact_details(string draft, string expected)
    {
        Assert.Contains(expected, PreservationChecker.ExtractProtected(draft));
    }

    [Theory]
    [InlineData("give me 3 ideas")]                    // single digits may become words
    [InlineData("e.g. something like that, i.e. short")] // abbreviations are not identifiers
    [InlineData("write a nice email to my professor")]
    public void Ignores_ordinary_words(string draft)
    {
        Assert.Empty(PreservationChecker.ExtractProtected(draft));
    }

    [Fact]
    public void Reports_only_what_was_dropped()
    {
        var draft = "fix the bug in src/api/users.ts where getUser returns null after 30 seconds";
        var rewrite = "Fix the bug in src/api/users.ts where the user lookup returns null after 30 seconds.";

        var missing = PreservationChecker.FindMissing(draft, rewrite);

        Assert.Equal(["getUser"], missing);
    }

    [Fact]
    public void File_name_inside_a_path_is_not_reported_twice()
    {
        var items = PreservationChecker.ExtractProtected("open src/app/main.py");
        Assert.Contains("src/app/main.py", items);
        Assert.DoesNotContain("main.py", items);
    }

    [Fact]
    public void Markers_are_not_treated_as_details()
    {
        Assert.Empty(PreservationChecker.ExtractProtected("explain [[BLOCK_1]] to me"));
    }

    [Fact]
    public void Curly_and_straight_apostrophes_compare_equal()
    {
        var missing = PreservationChecker.FindMissing("title \"Don't Panic\"", "Use the title \"Don\u2019t Panic\".");
        Assert.Empty(missing);
    }
}
