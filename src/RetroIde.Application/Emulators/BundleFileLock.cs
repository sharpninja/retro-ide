namespace RetroIde.Application.Emulators;

internal static class BundleFileLock
{
    internal static bool IsContended(IOException error)
    {
        const int WindowsSharingViolation = unchecked((int)0x80070020);
        const int LinuxWouldBlock = 11;
        return error.HResult == (OperatingSystem.IsWindows() ? WindowsSharingViolation : LinuxWouldBlock);
    }

    internal static async Task<FileStream> OpenExclusiveAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error) when (IsContended(error))
            {
                await Task.Delay(45, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
