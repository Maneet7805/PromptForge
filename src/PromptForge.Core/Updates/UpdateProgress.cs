using System.Globalization;

namespace PromptForge.Core.Updates;

public enum UpdateStage { Preparing, Downloading, Verifying }

/// <summary>Where a download is. <see cref="TotalBytes"/> is null when the server sent no size.</summary>
public readonly record struct UpdateProgress(UpdateStage Stage, long BytesReceived, long? TotalBytes)
{
    /// <summary>Whole percent 0–100, or null when the total is unknown.</summary>
    public int? Percent => TotalBytes is > 0 and var total
        ? (int)Math.Clamp(BytesReceived * 100 / total, 0, 100)
        : null;

    /// <summary>"38.2 of 90.1 MB", or "38.2 MB downloaded" when the total is unknown.</summary>
    public string Describe()
    {
        var received = Megabytes(BytesReceived);
        return TotalBytes is > 0 and var total
            ? $"{received} of {Megabytes(total)} MB"
            : $"{received} MB downloaded";
    }

    private static string Megabytes(long bytes) => (bytes / 1_048_576d).ToString("0.0", CultureInfo.InvariantCulture);
}
