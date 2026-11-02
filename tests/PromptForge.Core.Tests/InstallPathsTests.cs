using PromptForge.Core.Install;

namespace PromptForge.Core.Tests;

public class InstallPathsTests
{
    private static readonly string Local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pf-local-appdata"));
    private static readonly string Install = InstallPaths.InstallDir(Local);

    [Fact]
    public void The_install_folder_is_under_programs()
    {
        Assert.Equal(Path.Combine(Local, "Programs", "PromptForge"), Install);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_paths_are_never_safe(string? candidate)
    {
        Assert.False(InstallPaths.IsSafeToDelete(candidate, Local));
    }

    [Fact]
    public void Only_the_exact_install_folder_is_safe()
    {
        Assert.True(InstallPaths.IsSafeToDelete(Install, Local));
    }

    [Fact]
    public void A_trailing_separator_and_different_case_still_match()
    {
        Assert.True(InstallPaths.IsSafeToDelete(Install + Path.DirectorySeparatorChar, Local));
        Assert.True(InstallPaths.IsSafeToDelete(Install.ToUpperInvariant(), Local));
    }

    [Fact]
    public void A_dot_dot_path_that_lands_on_the_folder_is_the_same_folder()
    {
        Assert.True(InstallPaths.IsSafeToDelete(Path.Combine(Install, "sub", ".."), Local));
    }

    [Fact]
    public void Parents_roots_siblings_and_children_are_not_safe()
    {
        Assert.False(InstallPaths.IsSafeToDelete(Path.Combine(Local, "Programs"), Local));
        Assert.False(InstallPaths.IsSafeToDelete(Local, Local));
        Assert.False(InstallPaths.IsSafeToDelete(Path.GetPathRoot(Local), Local));
        Assert.False(InstallPaths.IsSafeToDelete(Path.Combine(Local, "Programs", "PromptForge2"), Local));
        Assert.False(InstallPaths.IsSafeToDelete(Path.Combine(Local, "Programs", "PromptForge-old"), Local));
        Assert.False(InstallPaths.IsSafeToDelete(Path.Combine(Install, "logs"), Local));
    }

    [Fact]
    public void A_dot_dot_path_that_escapes_the_folder_is_not_safe()
    {
        Assert.False(InstallPaths.IsSafeToDelete(Path.Combine(Install, "..", ".."), Local));
        Assert.False(InstallPaths.IsSafeToDelete(Path.Combine(Install, "..", "Other"), Local));
    }

    [Fact]
    public void A_missing_local_app_data_is_never_safe()
    {
        Assert.False(InstallPaths.IsSafeToDelete(Install, ""));
    }
}
