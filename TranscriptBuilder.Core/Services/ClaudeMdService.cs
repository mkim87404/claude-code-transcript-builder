using System.Text;

namespace TranscriptBuilder.Services;

// Reads, copies and creates the project's root CLAUDE.md (the only name Claude Code auto-loads).
public static class ClaudeMdService
{
    private const string FileName = "CLAUDE.md";

    public static bool ExistsAtRoot(string projectRoot) => File.Exists(PathAt(projectRoot));

    public static bool TryReadAtRoot(string projectRoot, out string content)
    {
        if (ExistsAtRoot(projectRoot))
        {
            content = File.ReadAllText(PathAt(projectRoot));
            return true;
        }

        content = string.Empty;
        return false;
    }

    // Byte-for-byte copy, always named CLAUDE.md regardless of the source's name; overwrite:false
    // throws IOException instead of clobbering a file that appeared since the caller's check.
    public static void CopyIntoProject(string sourcePath, string projectRoot) =>
        File.Copy(sourcePath, PathAt(projectRoot), overwrite: false);

    // Writes pasted text as BOM-less UTF-8; CreateNew likewise throws IOException rather than overwrite.
    public static void CreateAtRoot(string projectRoot, string content)
    {
        using var stream = new FileStream(PathAt(projectRoot), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string PathAt(string projectRoot) => Path.Combine(projectRoot, FileName);
}
