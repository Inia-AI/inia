using System.Globalization;
using System.Runtime.InteropServices;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// The limits the worker puts on itself on Linux before it reads a byte of any file, so that code a crafted file runs
/// inside PDFium inherits them and cannot lift them. The worker first stops being dumpable. A worker that starts as root,
/// as the app's image starts it, then switches to the user pdf-reader, which owns no file: it starts as root because a
/// process that starts as pdf-reader is dumpable by every other pdf-reader process until this point, and one of them
/// could open its memory then and keep the handle. Last, a seccomp filter refuses every new socket and io_uring, which
/// can open one past the filter, so the worker reaches no network; the calls that read or write another process; the
/// kernel keyring; and every new process, so nothing the worker starts outlives it. A worker that cannot set a limit
/// stops, and its document fails. On other systems the worker runs without these limits.
/// </summary>
internal static unsafe partial class WorkerSandbox
{
    private const string WORKER_USER = "pdf-reader";
    private const string PASSWD_FILE = "/etc/passwd";

    private const int PR_SET_DUMPABLE = 4;
    private const int PR_SET_NO_NEW_PRIVS = 38;
    private const nuint SECCOMP_SET_MODE_FILTER = 1;
    private const nuint SECCOMP_FILTER_FLAG_TSYNC = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SockFprog
    {
        public ushort Length;
        public SeccompFilter.SockFilter* Filter;
    }

    public static void Enter()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        SeccompFilter.Rules rules = SeccompFilter.RulesFor(RuntimeInformation.ProcessArchitecture);

        Succeed(SetProcessOption(PR_SET_DUMPABLE, 0, 0, 0, 0), "stop being dumpable");
        if (Environment.IsPrivilegedProcess)
        {
            SwitchToWorkerUser();
        }

        Succeed(SetProcessOption(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0), "give up new privileges");
        SeccompFilter.SockFilter[] filter = SeccompFilter.For(rules);
        fixed (SeccompFilter.SockFilter* instructions = filter)
        {
            var program = new SockFprog { Length = (ushort)filter.Length, Filter = instructions };
            nint result = SystemCall(rules.Seccomp, SECCOMP_SET_MODE_FILTER, SECCOMP_FILTER_FLAG_TSYNC, &program);
            if (result > 0)
            {
                throw new InvalidOperationException($"The PDF reader cannot install its filter on its thread {result}, so it stops before it reads the file.");
            }

            Succeed((int)result, "install its filter");
        }
    }

    /// <summary>
    /// Switches every thread to the worker's user, groups first. The kernel resets dumpability when the user changes,
    /// so the worker turns it off again.
    /// </summary>
    private static void SwitchToWorkerUser()
    {
        (uint userId, uint groupId) = WorkerAccount() ?? throw new InvalidOperationException(
            $"The PDF reader runs as root, and this host has no user {WORKER_USER} to switch to, so it stops before it reads the file. Add that user, as the app's image does.");
        Succeed(SetGroups(0, null), "drop root's groups");
        Succeed(SetGroupIds(groupId, groupId, groupId), $"switch to the group of {WORKER_USER}");
        Succeed(SetUserIds(userId, userId, userId), $"switch to the user {WORKER_USER}");
        Succeed(SetProcessOption(PR_SET_DUMPABLE, 0, 0, 0, 0), "stay undumpable as its own user");
    }

    /// <summary>The worker user's ids from the password file. The worker references none of our libraries, so it reads the file itself.</summary>
    private static (uint UserId, uint GroupId)? WorkerAccount()
    {
        foreach (string line in File.ReadLines(PASSWD_FILE))
        {
            string[] fields = line.Split(':');
            if (fields.Length > 3 && fields[0] == WORKER_USER
                && uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint userId)
                && uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint groupId))
            {
                return (userId, groupId);
            }
        }

        return null;
    }

    private static void Succeed(int result, string step)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"The PDF reader cannot {step} (error {Marshal.GetLastPInvokeError()}), so it stops before it reads the file.");
        }
    }

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int SetProcessOption(int option, nuint argument2, nuint argument3, nuint argument4, nuint argument5);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial nint SystemCall(nint number, nuint argument1, nuint argument2, void* argument3);

    [LibraryImport("libc", EntryPoint = "setgroups", SetLastError = true)]
    private static partial int SetGroups(nuint count, uint* groups);

    [LibraryImport("libc", EntryPoint = "setresgid", SetLastError = true)]
    private static partial int SetGroupIds(uint real, uint effective, uint saved);

    [LibraryImport("libc", EntryPoint = "setresuid", SetLastError = true)]
    private static partial int SetUserIds(uint real, uint effective, uint saved);
}
