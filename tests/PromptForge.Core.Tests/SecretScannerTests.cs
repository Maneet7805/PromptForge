using PromptForge.Core.Feedback;

namespace PromptForge.Core.Tests;

public class SecretScannerTests
{
    // All fake: built from repeated characters, never a real key.
    [Theory]
    [InlineData("use sk-abcdefghijklmnopqrstuvwx for the call")]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("GROQ=gsk_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("github_pat_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("key AKIAABCDEFGHIJKLMNOP here")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIE")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.abcdefghijkl")]
    [InlineData("password = hunter2hunter2")]
    [InlineData("{\"api_key\": \"abcdef123456\"}")]
    [InlineData("TOKEN: abcdefghij")]
    public void Credential_shapes_are_flagged(string text) =>
        Assert.True(SecretScanner.LooksLikeSecret(text));

    [Theory]
    [InlineData("explain how a password reset email works")]
    [InlineData("my token count is high and the secret menu is empty")]
    [InlineData("the sk-learn tutorial is short")]
    [InlineData("token: none")]
    [InlineData("")]
    public void Ordinary_text_is_not_flagged(string text) =>
        Assert.False(SecretScanner.LooksLikeSecret(text));
}
