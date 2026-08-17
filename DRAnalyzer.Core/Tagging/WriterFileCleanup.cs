using System.IO;

namespace DRAnalyzer.Core.Tagging;

/// <summary>
/// Best-effort cleanup for temporary writer artifacts.
///
/// Cleanup must never turn an already successful, validated File.Replace
/// into a user-visible write failure. If File.Replace itself fails, callers
/// intentionally keep the backup file for recovery.
/// </summary>
internal static class WriterFileCleanup
{
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stale hidden temp/backup file is preferable to masking the
            // actual writer result or the original exception.
        }
        catch (UnauthorizedAccessException)
        {
            // See above. Cleanup is deliberately best-effort only.
        }
    }
}
