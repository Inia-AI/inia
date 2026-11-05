using System.Runtime.InteropServices;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// The seccomp filter the PDF worker installs on itself on Linux, as classic BPF over the kernel's seccomp_data. The
/// worker compiles this file too; the server's tests read the filter here and run it.
/// </summary>
internal static class SeccompFilter
{
    private const ushort BPF_LOAD_WORD = 0x20;
    private const ushort BPF_JUMP_IF_EQUAL = 0x15;
    private const ushort BPF_JUMP_IF_AT_LEAST = 0x35;
    private const ushort BPF_JUMP_IF_ANY_BIT = 0x45;
    private const ushort BPF_RETURN = 0x06;
    private const uint SYSCALL_NUMBER_OFFSET = 0;
    private const uint ARCHITECTURE_OFFSET = 4;
    private const uint FIRST_ARGUMENT_LOW_WORD_OFFSET = 16;
    private const uint SECCOMP_RET_ALLOW = 0x7fff0000;
    private const uint SECCOMP_RET_KILL_PROCESS = 0x80000000;
    private const uint SECCOMP_RET_ERRNO = 0x00050000;
    private const uint EACCES = 13;
    private const uint ENOSYS = 38;
    private const uint X32_SYSCALL_BIT = 0x40000000;
    private const uint CLONE_THREAD = 0x00010000;
    private const uint CLONE3 = 435;

    /// <summary>
    /// The system calls the filter needs on one architecture. <see cref="Refused"/> names each call it refuses. x86-64 also
    /// takes the x32 numbers, which the filter refuses as a whole, since each is another way to make those calls.
    /// </summary>
    internal sealed record Rules(uint AuditArchitecture, nint Seccomp, uint CloneCall, (string Name, uint Number)[] Refused, bool HasX32);

    private static readonly Rules _x64 = new(
        AuditArchitecture: 0xC000003E,
        Seccomp: 317,
        CloneCall: 56,
        Refused:
        [
            ("socket", 41), ("io_uring_setup", 425), ("ptrace", 101), ("process_vm_readv", 310), ("process_vm_writev", 311),
            ("pidfd_getfd", 438), ("add_key", 248), ("request_key", 249), ("keyctl", 250), ("fork", 57), ("vfork", 58),
        ],
        HasX32: true);

    private static readonly Rules _arm64 = new(
        AuditArchitecture: 0xC00000B7,
        Seccomp: 277,
        CloneCall: 220,
        Refused:
        [
            ("socket", 198), ("io_uring_setup", 425), ("ptrace", 117), ("process_vm_readv", 270), ("process_vm_writev", 271),
            ("pidfd_getfd", 438), ("add_key", 217), ("request_key", 218), ("keyctl", 219),
        ],
        HasX32: false);

    private enum Outcome
    {
        Next,
        Allow,
        Refuse,
        NotImplemented,
        Kill,
    }

    private readonly record struct Step(ushort Code, uint Value, Outcome WhenTrue = Outcome.Next, Outcome WhenFalse = Outcome.Next);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SockFilter
    {
        public ushort Code;
        public byte JumpIfTrue;
        public byte JumpIfFalse;
        public uint Value;
    }

    public static Rules RulesFor(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.X64 => _x64,
            Architecture.Arm64 => _arm64,
            _ => throw new PlatformNotSupportedException($"The PDF reader has no sandbox for {architecture}."),
        };
    }

    /// <summary>
    /// Kills a process of another architecture; refuses the <see cref="Rules.Refused"/> calls, the x32 calls and a
    /// clone that starts a process rather than a thread; answers clone3 as not implemented, so the C library starts its
    /// threads with clone; and allows the rest.
    /// </summary>
    public static SockFilter[] For(Rules table)
    {
        List<Step> steps =
        [
            new(BPF_LOAD_WORD, ARCHITECTURE_OFFSET),
            new(BPF_JUMP_IF_EQUAL, table.AuditArchitecture, WhenFalse: Outcome.Kill),
            new(BPF_LOAD_WORD, SYSCALL_NUMBER_OFFSET),
        ];
        if (table.HasX32)
        {
            steps.Add(new(BPF_JUMP_IF_AT_LEAST, X32_SYSCALL_BIT, WhenTrue: Outcome.Refuse));
        }

        foreach ((_, uint number) in table.Refused)
        {
            steps.Add(new(BPF_JUMP_IF_EQUAL, number, WhenTrue: Outcome.Refuse));
        }

        steps.Add(new(BPF_JUMP_IF_EQUAL, CLONE3, WhenTrue: Outcome.NotImplemented));
        steps.Add(new(BPF_JUMP_IF_EQUAL, table.CloneCall, WhenFalse: Outcome.Allow));
        steps.Add(new(BPF_LOAD_WORD, FIRST_ARGUMENT_LOW_WORD_OFFSET));
        steps.Add(new(BPF_JUMP_IF_ANY_BIT, CLONE_THREAD, Outcome.Allow, Outcome.Refuse));

        Dictionary<Outcome, (int Index, uint Return)> returns = new()
        {
            [Outcome.Allow] = (steps.Count, SECCOMP_RET_ALLOW),
            [Outcome.Refuse] = (steps.Count + 1, SECCOMP_RET_ERRNO | EACCES),
            [Outcome.NotImplemented] = (steps.Count + 2, SECCOMP_RET_ERRNO | ENOSYS),
            [Outcome.Kill] = (steps.Count + 3, SECCOMP_RET_KILL_PROCESS),
        };

        var filter = new SockFilter[steps.Count + returns.Count];
        for (int index = 0; index < steps.Count; index++)
        {
            Step step = steps[index];
            filter[index] = new SockFilter
            {
                Code = step.Code,
                Value = step.Value,
                JumpIfTrue = Skip(index, step.WhenTrue),
                JumpIfFalse = Skip(index, step.WhenFalse),
            };
        }

        foreach ((int index, uint value) in returns.Values)
        {
            filter[index] = new SockFilter { Code = BPF_RETURN, Value = value };
        }

        return filter;

        byte Skip(int from, Outcome target)
        {
            return target == Outcome.Next ? (byte)0 : (byte)(returns[target].Index - from - 1);
        }
    }
}
