using System.Text.RegularExpressions;
using PromptForge.Core.Feedback;

namespace PromptForge.Core.Tests;

public class BadRewriteLinkTests
{
    private static BadRewriteLink Build(string? original = null, string? rewrite = null) =>
        BadRewriteLink.Build("1.1.0", "Standard", "some/model", original, rewrite);

    private static Dictionary<string, string> Query(string url) =>
        url[(url.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public void Without_opt_in_only_the_template_and_details_are_filled()
    {
        var link = Build();
        var query = Query(link.Url);
        Assert.False(link.TextIncluded);
        Assert.Equal("bad_rewrite.yml", query["template"]);
        Assert.Equal("1.1.0, Standard, some/model", query["details"]);
        Assert.False(query.ContainsKey("original"));
        Assert.False(query.ContainsKey("rewrite"));
    }

    [Fact]
    public void Text_with_awkward_characters_survives_the_round_trip()
    {
        var original = "fix this & that #1\n\"quoted\" 100% café 😀 = x?";
        var rewrite = "Fix this & that #1.\nKeep: \"quoted\", 100%, café 😀.";
        var link = Build(original, rewrite);
        var query = Query(link.Url);
        Assert.True(link.TextIncluded);
        Assert.False(link.Truncated);
        Assert.Equal(original, query["original"]);
        Assert.Equal(rewrite, query["rewrite"]);
    }

    [Fact]
    public void Windows_newlines_become_unix_newlines()
    {
        var link = Build("a\r\nb\rc", "x");
        Assert.Equal("a\nb\nc", Query(link.Url)["original"]);
    }

    [Fact]
    public void A_very_long_draft_is_cut_with_a_note_and_the_link_stays_under_the_limit()
    {
        var link = Build(new string('a', 20_000), "short rewrite");
        var query = Query(link.Url);
        Assert.True(link.Truncated);
        Assert.True(link.Url.Length <= BadRewriteLink.MaxUrlLength);
        Assert.Matches(@"\[\.\.\.cut by PromptForge: [\d,]+ of 20,000 characters shown\]$", query["original"]);
        Assert.Equal("short rewrite", query["rewrite"]);
    }

    [Fact]
    public void A_short_field_leaves_its_room_to_the_long_one()
    {
        var both = Build(new string('a', 20_000), new string('b', 20_000));
        var oneShort = Build(new string('a', 20_000), "short");
        Assert.True(Shown(Query(oneShort.Url)["original"]) > Shown(Query(both.Url)["original"]));
    }

    [Fact]
    public void Both_long_fields_get_a_similar_share()
    {
        var query = Query(Build(new string('a', 20_000), new string('b', 20_000)).Url);
        Assert.InRange(Shown(query["original"]) - Shown(query["rewrite"]), -50, 50);
    }

    [Fact]
    public void Heavily_encoded_text_still_fits()
    {
        var link = Build(string.Concat(Enumerable.Repeat("é\n😀", 5_000)), string.Concat(Enumerable.Repeat("日本語\n", 5_000)));
        Assert.True(link.Url.Length <= BadRewriteLink.MaxUrlLength);
        Assert.True(link.Truncated);
    }

    [Fact]
    public void A_cut_never_splits_an_emoji()
    {
        var link = Build(string.Concat(Enumerable.Repeat("😀", 10_000)), "x");
        var original = Query(link.Url)["original"];
        var body = original[..original.IndexOf("\n[...cut", StringComparison.Ordinal)];
        Assert.DoesNotContain('�', body);
        Assert.Equal(0, body.Length % 2);
        Assert.True(body.Length > 0 && char.IsLowSurrogate(body[^1]));
    }

    [Fact]
    public void A_lone_surrogate_does_not_throw()
    {
        var link = Build("bad \uD83D end", "ok");
        Assert.Equal("bad � end", Query(link.Url)["original"]);
    }

    [Fact]
    public void A_credential_in_either_text_blanks_both_fields()
    {
        foreach (var (o, r) in new[] { ("key sk-abcdefghijklmnopqrstuvwx", "fine"), ("fine", "password = hunter2hunter2") })
        {
            var link = Build(o, r);
            var query = Query(link.Url);
            Assert.True(link.SkippedForSecret);
            Assert.False(link.TextIncluded);
            Assert.False(query.ContainsKey("original"));
            Assert.False(query.ContainsKey("rewrite"));
            Assert.DoesNotContain("sk-abc", link.Url);
        }
    }

    [Fact]
    public void Details_are_clipped()
    {
        var link = BadRewriteLink.Build("1.1.0", "Standard", new string('m', 5_000), null, null);
        Assert.True(Query(link.Url)["details"].Length <= 200);
    }

    [Fact]
    public void Field_names_match_the_issue_form()
    {
        var yml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "ISSUE_TEMPLATE", BadRewriteLink.Template));
        foreach (var id in new[] { BadRewriteLink.OriginalField, BadRewriteLink.RewriteField, BadRewriteLink.DetailsField })
            Assert.Contains($"id: {id}", yml);
    }

    private static int Shown(string field) =>
        int.Parse(Regex.Match(field, @"cut by PromptForge: ([\d,]+) of").Groups[1].Value.Replace(",", ""));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github", "ISSUE_TEMPLATE"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }
}
