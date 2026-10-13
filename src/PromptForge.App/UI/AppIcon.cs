namespace PromptForge.App.UI;

internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon is not null) return _icon;
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PromptForge.App.Assets.PromptForge.ico");
        _icon = stream is null ? SystemIcons.Application : new Icon(stream);
        return _icon;
    }
}
