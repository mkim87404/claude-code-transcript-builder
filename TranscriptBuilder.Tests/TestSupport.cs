using TranscriptBuilder.Models;
using TranscriptBuilder.Services;

// Safe: every test works in its own temp folder and shares no state.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace TranscriptBuilder.Tests;

// A throwaway project folder on the real filesystem, deleted when disposed.
internal sealed class TempProject : IDisposable
{
    public string Root { get; } = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "TranscriptBuilderTests", Guid.NewGuid().ToString("N"))).FullName;

    public string PathOf(string fileName) => Path.Combine(Root, fileName);

    public string TranscriptPath => PathOf("AI_TRANSCRIPT.txt");

    public string ReadTranscript() => File.ReadAllText(TranscriptPath);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal static class Flow
{
    // The same sequence MainWindow performs when Log is pressed (minus clipboard and UI text).
    public static SessionState Log(SessionState session, string text)
    {
        var entry = EntryClassifier.Classify(session, text);

        if (session.NextEntryKind == NextEntryKind.ClaudeMd && !ClaudeMdService.ExistsAtRoot(session.ProjectRoot))
        {
            ClaudeMdService.CreateAtRoot(session.ProjectRoot, entry.Body);
        }

        TranscriptService.AppendEntry(session, entry.Heading, entry.Body);
        return entry.IsSessionExit ? session.AfterMarkerLogged() : session.AfterEntryLogged();
    }
}
