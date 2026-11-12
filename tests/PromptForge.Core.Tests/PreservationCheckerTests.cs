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
    [InlineData("write an ad for [Brand] aimed at [target audience]", "[Brand]")]
    [InlineData("write an ad for [Brand] aimed at [target audience]", "[target audience]")]
    [InlineData("start the email with Hi {{ first_name }},", "{{ first_name }}")]
    [InlineData("act as ${Role:teacher} for this lesson", "${Role:teacher}")]
    public void Extracts_exact_details(string draft, string expected)
    {
        Assert.Contains(expected, PreservationChecker.ExtractProtected(draft));
    }

    [Theory]
    [InlineData("give me 3 ideas")]                    // single digits may become words
    [InlineData("e.g. something like that, i.e. short")] // abbreviations are not identifiers
    [InlineData("write a nice email to my professor")]
    [InlineData("as shown in [1], it is faster")]            // citations are not slots
    [InlineData("- [x] buy milk")]                           // checkboxes are not slots
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

    [Theory]
    [InlineData("summarise this", "Summarise this in 50 words.", "50")]
    [InlineData("list sales for the last quarter", "List sales for the last 5 years.", "5")]
    [InlineData("it takes 3 weeks", "It takes 21 days.", "21")] // arithmetic looks invented; the warning is advice only
    [InlineData("a tip for a day", "Rate it 4.5 out of 10 points.", "4.5, 10")]
    [InlineData("a tip for a day", "Rate it out of 10.", "")] // known gap: a number ending a sentence is skipped, same as the quality check
    public void Numbers_the_draft_never_had_are_found(string draft, string rewrite, string expected) =>
        Assert.Equal(expected, string.Join(", ", PreservationChecker.FindAddedNumbers(draft, rewrite)));

    [Theory]
    [InlineData("keep it under 40 lines", "Keep it under 40 lines.")]
    [InlineData("fix this", "Fix this. Steps:\n1. Read it.\n2. Fix it.")] // list markers are not numbers
    [InlineData("use python 3.11", "Use Python 3.11 and numpy2.")]
    [InlineData("a 2026 plan", "Write a 2026 plan; 2026 is the year.")]
    public void Numbers_that_were_in_the_draft_or_are_not_numbers_are_ignored(string draft, string rewrite) =>
        Assert.Empty(PreservationChecker.FindAddedNumbers(draft, rewrite));
}

public class TemplatePlaceholderTests
{
    [Fact]
    public void Dropped_placeholder_is_reported()
    {
        var draft = "write a tagline for [Brand]";
        var rewrite = "Write a short, memorable tagline for the brand.";
        Assert.Contains("[Brand]", PreservationChecker.FindMissing(draft, rewrite));
    }

    [Fact]
    public void Markdown_links_and_indexing_are_not_placeholders()
    {
        var items = PreservationChecker.ExtractProtected("see [the docs](https://example.com) and fix array[i]");
        Assert.DoesNotContain("[the docs]", items);
        Assert.DoesNotContain("[i]", items);
    }

    [Fact]
    public void Capitalised_words_dropped_from_the_rewrite_are_reported()
    {
        var draft = "one of them, Mei Ling, is afraid of change, so keep the screen simple. order by 20 February.";
        var rewrite = "Keep the screen simple. Order by 20.";

        var missing = PreservationChecker.FindMissingDetails(draft, rewrite);

        Assert.Contains("Mei Ling", missing);
        Assert.Contains("February", missing);
    }

    [Fact]
    public void Capitalised_words_kept_in_any_letter_case_are_not_reported()
    {
        var draft = "ask Aisha about the Python version";
        var rewrite = "Ask AISHA about the python version.";

        Assert.Empty(PreservationChecker.FindMissingDetails(draft, rewrite));
    }

    [Theory]
    [InlineData("Please fix the login page.")]                       // first word of the draft
    [InlineData("it fails. The cause is unknown. Give me a fix.")]   // first words of sentences
    [InlineData("steps:\n- Open the app\n1. Click save")]           // first words of lines and list items
    [InlineData("fix this: ```\nSettings Panel Broken\n``` now")]    // inside a code block
    [InlineData("use `Foo Bar` here")]                               // inside inline code
    public void Sentence_starts_and_code_are_not_treated_as_names(string draft)
    {
        Assert.Empty(PreservationChecker.ExtractCapitalised(draft));
    }

    [Fact]
    public void A_name_that_starts_a_sentence_is_found_only_when_it_has_two_words()
    {
        Assert.Contains("Mei Ling", PreservationChecker.ExtractCapitalised("she is nervous. Mei Ling is afraid of change."));
        Assert.Empty(PreservationChecker.ExtractCapitalised("she is nervous. Aisha is afraid of change."));
    }

    [Fact]
    public void Exact_detail_checks_used_by_the_quality_check_do_not_include_capitalised_words()
    {
        Assert.Empty(PreservationChecker.FindMissing("ask Aisha at the Clinic", "ask someone."));
    }
}
