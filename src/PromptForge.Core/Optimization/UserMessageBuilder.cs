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

    public static string BuildRepairMessage(IReadOnlyList<string> missing)
    {
        var list = string.Join("\n", missing.Select(m => $"- {m}"));
        return $"""
            Your rewrite left out these exact details from the draft:
            {list}

            Rewrite again so each of them appears exactly as written, in the place it had in the draft. Don't add an instruction telling the AI to use or include them; just put them back. Change nothing else. Respond with the same JSON object shape.
            """;
    }
}
