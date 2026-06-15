using System.Text;

namespace GajaeCode.AirgapInstaller;

internal static class FileOperations
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static void MoveReplacingWithRetry(string sourcePath, string destinationPath)
    {
        RunWithLockRetry(() => File.Move(sourcePath, destinationPath, true));
    }

    public static void DeleteWithRetry(string path)
    {
        RunWithLockRetry(() => File.Delete(path));
    }

    public static void CopyReplacingWithRetry(string sourcePath, string destinationPath)
    {
        RunWithLockRetry(() => File.Copy(sourcePath, destinationPath, true));
    }

    public static void WriteAllTextReplacingWithRetry(string path, string contents, Encoding encoding)
    {
        var temporaryPath = $"{path}.new-{Guid.NewGuid():N}";
        try
        {
            RunWithLockRetry(() => File.WriteAllText(temporaryPath, contents, encoding));
            MoveReplacingWithRetry(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                DeleteWithRetry(temporaryPath);
            }
        }
    }

    public static void AppendAllTextWithRetry(string path, string contents, Encoding encoding)
    {
        RunWithLockRetry(() => File.AppendAllText(path, contents, encoding));
    }

    private static void RunWithLockRetry(Action operation)
    {
        const int maxAttempts = 10;
        var delayMilliseconds = 100;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (IOException exception) when (
                attempt < maxAttempts &&
                IsSharingOrLockViolation(exception))
            {
                Thread.Sleep(delayMilliseconds);
                delayMilliseconds = Math.Min(delayMilliseconds * 2, 1_500);
            }
        }
    }

    private static bool IsSharingOrLockViolation(IOException exception)
    {
        var windowsError = exception.HResult & 0xFFFF;
        return windowsError is ErrorSharingViolation or ErrorLockViolation;
    }
}
