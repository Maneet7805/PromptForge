using System.Text;

namespace PromptForge.Core.Optimization;

/// <summary>Builds the per-request user message. Everything that varies lives here, not in the system prompt.</summary>
public static class UserMessageBuilder
{
    public static string Build(OptimizeRequest request, MaskResult masked)
    {
        // Always "\n", never Environment.NewLine: the message must be identical on every OS.
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        Line("<settings>");
        Line($"mode: {request.Mode.ToString().ToLowerInvariant()}");
        Line(request.TaskOverride == TaskType.Auto
            ? "task_type: decide from the draft"
            : $"task_type: {request.TaskOverride.ToString().ToLowerInvariant()} (chosen by the user)");
        if (!string.IsNullOrWhiteSpace(request.WrittenIn))
            Line($"written_in: {request.WrittenIn.Trim()}");
        Line("</settings>");

        if (!string.IsNullOrWhiteSpace(request.PersonalContext))
        {
            Line();
            Line("<about_user>");
            Line("Standing background about the user. Use it only where it is clearly relevant to this draft; never use it to fill in missing task details.");
            Line(request.PersonalContext.Trim());
            Line("</about_user>");
        }

        if (masked.HasBlocks)
        {
            Line();
            Line("<held_back_material>");
            foreach (var block in masked.Blocks)
                Line($"{block.Marker}: {block.Description}");
            Line("</held_back_material>");
        }

        Line();
        Line("<draft>");
        Line(masked.Text.Trim());
        Line("</draft>");

        // Kept out of <draft> so the answers aren't rewritten as more draft, and so a short answer
        // like "uni essay, 800" arrives next to the question that gives it meaning.
        if (!string.IsNullOrWhiteSpace(request.Answers))
        {
            Line();
            Line("<user_answers>");
            Line("The user's reply to your earlier questions about this draft, or details they added. It counts as something the user said: work it into the rewrite where it belongs, don't rewrite it as more draft, and don't ask again about anything it settles.");
            if (request.PreviousQuestions.Count > 0)
            {
                Line("Earlier questions:");
                foreach (var question in request.PreviousQuestions)
                    Line($"- {question.Trim()}");
            }
            Line("Reply:");
            Line(request.Answers.Trim());
            Line("</user_answers>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// System message for the repair call. The full rewriting rules are not needed to put a few details back, and
    /// they alone cost about 2,000 tokens, which matters on a free tier that allows 8,000 per minute.
    /// </summary>
    public const string RepairInstructions =
        "You fix a rewritten prompt that left out exact details from the user's original draft. " +
        "Put the missing details back and change nothing else. " +
        "Respond with only one JSON object, no other text: {\"prompt\": \"the corrected prompt\"}";

    /// <summary>
    /// The repair request: the rewrite plus, for each missing detail, the short piece of the draft it came from.
    /// Not the whole draft, so the request stays small even when the draft is long.
    /// </summary>
    public static string BuildRepairRequest(string rewrite, IReadOnlyList<string> missing, string draft)
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        Line("<rewrite>");
        Line(rewrite.Trim());
        Line("</rewrite>");
        Line();
        Line("The rewrite above left out these exact details from the user's draft, each shown with the part of the draft it came from:");
        foreach (var item in missing.Take(MaxRepairItems))
        {
            var excerpt = ExcerptAround(draft, item);
            Line(excerpt is null ? $"- {item}" : $"- {item}   (draft: \"{excerpt}\")");
        }
        Line();
        Line("Put each of them back exactly as written, inside a full sentence in the part of the rewrite that matches where the draft used it, changing that sentence only as much as needed. Never leave a detail alone on its own line or after a full stop. Don't add an instruction telling the AI to use or include them; say only what the draft said about them. Change nothing else, and don't shorten or drop anything else from the rewrite.");
        return sb.ToString();
    }

    private const int MaxRepairItems = 12;
    private const int ExcerptChars = 200;

    /// <summary>The line of the draft holding <paramref name="item"/>, cut to a window around it. Null when it can't be found as written.</summary>
    private static string? ExcerptAround(string draft, string item)
    {
        var at = draft.IndexOf(item, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var lineStart = draft.LastIndexOf('\n', at) + 1;
        var lineEnd = draft.IndexOf('\n', at);
        if (lineEnd < 0) lineEnd = draft.Length;
        var start = Math.Max(lineStart, at - ExcerptChars / 2);
        var end = Math.Min(lineEnd, Math.Max(at + item.Length, start + ExcerptChars));
        var text = string.Join(' ', draft[start..end].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (start > lineStart ? "..." : "") + text + (end < lineEnd ? "..." : "");
    }
}
