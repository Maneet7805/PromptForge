using System.Runtime.InteropServices;

namespace PromptForge.App.TextAccess;

/// <summary>
/// Puts text on the clipboard with Win32 delayed rendering: the clipboard only holds a promise, and
/// Windows asks this window for the actual text (WM_RENDERFORMAT) when an app pastes. That tells us
/// exactly when the target app has read it, so the user's clipboard is restored only afterwards.
/// Without this, apps that read the clipboard asynchronously (Windows 11 Notepad) paste stale data.
/// </summary>
internal sealed partial class DelayedClipboard : NativeWindow, IDisposable
{
    private const int WM_RENDERFORMAT = 0x0305;
    private const int WM_RENDERALLFORMATS = 0x0306;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private static readonly uint ExcludeFromMonitors = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint CanIncludeInHistory = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    private static readonly uint CanUploadToCloud = RegisterClipboardFormatW("CanUploadToCloudClipboard");

    private string? _text;
    private bool _armed;
    private TaskCompletionSource? _consumed;

    public DelayedClipboard() => CreateHandle(new CreateParams { Caption = "PromptForge.Clipboard" });

    /// <summary>Offers text on the clipboard without rendering it yet. Returns false if the clipboard is locked.</summary>
    public bool Offer(string text)
    {
        _text = text;
        _armed = false;
        _consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        for (var i = 0; i < 8; i++)
        {
            if (OpenClipboard(Handle))
            {
                try
                {
                    EmptyClipboard();
                    SetClipboardData(CF_UNICODETEXT, IntPtr.Zero); // delayed: rendered on request
                    SetDword(ExcludeFromMonitors, 0);
                    SetDword(CanIncludeInHistory, 0);
                    SetDword(CanUploadToCloud, 0);
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(25);
        }
        return false;
    }

    /// <summary>Call right before sending Ctrl+V; reads before this point (clipboard monitors) don't count.</summary>
    public Task Arm()
    {
        _armed = true;
        return _consumed?.Task ?? Task.CompletedTask;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_RENDERFORMAT when (uint)m.WParam.ToInt64() == CF_UNICODETEXT:
                Render();
                if (_armed) _consumed?.TrySetResult();
                return;
            case WM_RENDERALLFORMATS:
                if (OpenClipboard(Handle))
                {
                    try { if (GetClipboardOwner() == Handle) Render(); }
                    finally { CloseClipboard(); }
                }
                return;
        }
        base.WndProc(ref m);
    }

    private void Render()
    {
        var text = _text ?? "";
        var bytes = (text.Length + 1) * 2;
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
        if (handle == IntPtr.Zero) return;
        var ptr = GlobalLock(handle);
        try
        {
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            Marshal.WriteInt16(ptr, text.Length * 2, 0);
        }
        finally
        {
            GlobalUnlock(handle);
        }
        SetClipboardData(CF_UNICODETEXT, handle); // ownership passes to the system
    }

    private static void SetDword(uint format, int value)
    {
        if (format == 0) return;
        var handle = GlobalAlloc(GMEM_MOVEABLE, 4);
        if (handle == IntPtr.Zero) return;
        Marshal.WriteInt32(GlobalLock(handle), value);
        GlobalUnlock(handle);
        SetClipboardData(format, handle);
    }

    public void Dispose() => DestroyHandle();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetClipboardOwner();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);
}
