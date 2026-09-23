namespace TranscriptBuilder.Models;

// The saved CLAUDE.md-compliance audit prompt, inserted into the compose box on request.
public static class AuditPrompt
{
    public const string Base =
        "Audit this codebase against CLAUDE.md. Work through it rule by rule in file order — " +
        "every bullet, including ones you expect to be satisfied — and judge each as compliant, " +
        "violated, or not applicable. Work from the rules' actual text; don't paraphrase or " +
        "summarise them. Report only the violations and the not-applicables, each with a " +
        "file:line citation, one line of reasoning, and for violations one line on the fix " +
        "you'd apply. Don't fix anything yet — list findings and wait for me to choose which to action.";

    // The literal placeholder at the end of the limit line appended after the prompt.
    public const string AreaPlaceholder = "<area>";

    public const string LimitLine = $"Limit this to {AreaPlaceholder}";

    // The prompt Insert Audit Prompt actually uses: the user's saved wording, else the built-in one.
    public static string Effective(string? customText) => customText ?? Base;

    // What to persist from the editor: null for empty or default-identical text, so "using the
    // default" is one state and a future change to Base reaches anyone who never customized it.
    public static string? NormalizeCustom(string editedText)
    {
        var text = editedText.Trim();
        return text.Length == 0 || text == Base ? null : text;
    }
}
