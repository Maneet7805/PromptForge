using System.Text;

namespace PromptForge.Core.Optimization;

/// <summary>Builds the per-request user message. Everything that varies lives here, not in the system prompt.</summary>
public static class UserMessageBuilder
{
    public static string Build(OptimizeRequest request, MaskResult masked)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<settings>");
        sb.AppendLine($"mode: {request.Mode.ToString().ToLowerInvariant()}");
        sb.AppendLine(request.TaskOverride == TaskType.Auto
            ? "task_type: decide from the draft"
            : $"task_type: {request.TaskOverride.ToString().ToLowerInvariant()} (chosen by the user)");
        if (!string.IsNullOrWhiteSpace(request.WrittenIn))
            sb.AppendLine($"written_in: {request.WrittenIn.Trim()}");
        sb.AppendLine("</settings>");

        if (!string.IsNullOrWhiteSpace(request.PersonalContext))
        {
            sb.AppendLine();
            sb.AppendLine("<about_user>");
            sb.AppendLine("Standing background about the user. Use it only where it is clearly relevant to this draft; never use it to fill in missing task details.");
            sb.AppendLine(request.PersonalContext.Trim());
            sb.AppendLine("</about_user>");
        }

        if (masked.HasBlocks)
        {
            sb.AppendLine();
            sb.AppendLine("<held_back_material>");
            foreach (var block in masked.Blocks)
                sb.AppendLine($"{block.Marker}: {block.Description}");
            sb.AppendLine("</held_back_material>");
        }

        sb.AppendLine();
        sb.AppendLine("<draft>");
        sb.AppendLine(masked.Text.Trim());
        sb.AppendLine("</draft>");
        return sb.ToString();
    }

    public static string BuildRepairMessage(IReadOnlyList<string> missing)
    {
        var list = string.Join("\n", missing.Select(m => $"- {m}"));
        return $"""
            Your rewrite left out these exact details from the draft:
            {list}

            Rewrite again so each of them appears exactly as written, in the place it belongs. Change nothing else. Respond with the same JSON object shape.
            """;
    }
}
