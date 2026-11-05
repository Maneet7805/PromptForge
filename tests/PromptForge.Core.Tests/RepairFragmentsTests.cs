using PromptForge.Core.Optimization;

namespace PromptForge.Core.Tests;

public class RepairFragmentsTests
{
    [Theory]
    [InlineData("Keep every row (never drop rows). 20", "20")]
    [InlineData("Keep every row.\n- 20", "20")]
    [InlineData("20", "20")]
    [InlineData("Closes 23:00 11", "11")]
    public void A_detail_without_a_sentence_around_it_is_a_bare_fragment(string repaired, string detail)
    {
        Assert.True(RepairFragments.HasBareDetail(repaired, [detail]));
    }

    [Theory]
    [InlineData("Keep all 20 rows in the output.", "20")]
    [InlineData("Read src/api/users.ts first. Then fix getUser so it stops returning null.", "getUser")]
    [InlineData("Version 3.11 is required for this step.", "3.11")]
    public void A_detail_inside_a_sentence_is_not_a_bare_fragment(string repaired, string detail)
    {
        Assert.False(RepairFragments.HasBareDetail(repaired, [detail]));
    }

    [Fact]
    public void A_detail_the_repair_did_not_restore_is_not_judged()
    {
        Assert.False(RepairFragments.HasBareDetail("Fix the users module.", ["20"]));
    }
}
