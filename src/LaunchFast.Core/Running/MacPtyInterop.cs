using System.Runtime.InteropServices;

namespace LaunchFast.Core.Running;

/// <summary>
/// Raw libc P/Invoke surface for the macOS PTY backend. Source-generated marshalling via
/// <see cref="LibraryImportAttribute"/> where practical; the opaque
/// <c>posix_spawn_file_actions_t</c> is passed as an <see cref="IntPtr"/> to a caller-owned
/// native buffer (its concrete size is ABI-private, so we over-allocate generously).
/// </summary>
internal static partial class MacPtyInterop
{
    const string Libc = "libSystem.dylib";

    // posix_spawn_file_actions_t is an opaque pointer-sized handle on macOS, but to be safe
    // against ABI changes we allocate a comfortably large zeroed buffer to back it.
    public const int FileActionsSize = 256;

    public const int Stdin = 0;
    public const int Stdout = 1;
    public const int Stderr = 2;

    public const int SIGTERM = 15;
    public const int SIGKILL = 9;

    // openpty(int *amaster, int *aslave, char *name, struct termios *termp, struct winsize *winp)
    [LibraryImport(Libc, SetLastError = true)]
    public static partial int openpty(
        out int amaster,
        out int aslave,
        IntPtr name,
        IntPtr termp,
        IntPtr winp);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial int posix_spawn_file_actions_init(IntPtr fileActions);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial int posix_spawn_file_actions_destroy(IntPtr fileActions);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial int posix_spawn_file_actions_adddup2(
        IntPtr fileActions, int filedes, int newfiledes);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial int posix_spawn_file_actions_addclose(
        IntPtr fileActions, int filedes);

    // macOS 10.15+. Changes the child's working directory before exec.
    [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int posix_spawn_file_actions_addchdir_np(
        IntPtr fileActions, string path);

    // posix_spawn(pid_t *pid, const char *path, const posix_spawn_file_actions_t *file_actions,
    //             const posix_spawnattr_t *attrp, char *const argv[], char *const envp[])
    [LibraryImport(Libc, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int posix_spawn(
        out int pid,
        string path,
        IntPtr fileActions,
        IntPtr attrp,
        IntPtr[] argv,
        IntPtr[] envp);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial nint read(int fd, byte[] buf, nuint count);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial nint write(int fd, byte[] buf, nuint count);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport(Libc, SetLastError = true)]
    public static partial int kill(int pid, int sig);

    // waitpid(pid_t pid, int *status, int options)
    [LibraryImport(Libc, SetLastError = true)]
    public static partial int waitpid(int pid, out int status, int options);
}
