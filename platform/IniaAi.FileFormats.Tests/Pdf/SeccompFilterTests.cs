using System.Buffers.Binary;
using System.Runtime.InteropServices;
using IniaAi.FileFormats.Pdf.Pdfium;

namespace IniaAi.FileFormats.Tests.Pdf;

/// <summary>
/// Runs the worker's seccomp filter for each architecture through a classic BPF interpreter, so the filter of an
/// architecture this machine does not run is checked too. Each answer is the kernel's: allow, an error number, or kill.
/// </summary>
public class SeccompFilterTests
{
    private const uint X64 = 0xC000003E;
    private const uint ARM64 = 0xC00000B7;
    private const uint ALLOWED = 0x7fff0000;
    private const uint REFUSED = 0x00050000 | 13;
    private const uint NOT_IMPLEMENTED = 0x00050000 | 38;
    private const uint KILLED = 0x80000000;
    private const ulong THREAD_FLAGS = 0x003d0f00;
    private const ulong PROCESS_FLAGS = 17;
    private const uint X32 = 0x40000000;

    private const ushort LOAD_WORD = 0x20;
    private const ushort JUMP_IF_EQUAL = 0x15;
    private const ushort JUMP_IF_AT_LEAST = 0x35;
    private const ushort JUMP_IF_ANY_BIT = 0x45;
    private const ushort RETURN = 0x06;

    [Theory]
    [InlineData("read", 0u, 0ul, ALLOWED)]
    [InlineData("socket", 41u, 0ul, REFUSED)]
    [InlineData("io_uring_setup", 425u, 0ul, REFUSED)]
    [InlineData("ptrace", 101u, 0ul, REFUSED)]
    [InlineData("process_vm_readv", 310u, 0ul, REFUSED)]
    [InlineData("process_vm_writev", 311u, 0ul, REFUSED)]
    [InlineData("pidfd_getfd", 438u, 0ul, REFUSED)]
    [InlineData("keyctl", 250u, 0ul, REFUSED)]
    [InlineData("fork", 57u, 0ul, REFUSED)]
    [InlineData("vfork", 58u, 0ul, REFUSED)]
    [InlineData("clone of a thread", 56u, THREAD_FLAGS, ALLOWED)]
    [InlineData("clone of a process", 56u, PROCESS_FLAGS, REFUSED)]
    [InlineData("clone3", 435u, 0ul, NOT_IMPLEMENTED)]
    [InlineData("x32 socket", X32 + 41u, 0ul, REFUSED)]
    [InlineData("x32 read", X32, 0ul, REFUSED)]
    public void X64Filter_AnswersEachCall(string call, uint number, ulong firstArgument, uint answer)
    {
        Assert.True(answer == Run(Architecture.X64, X64, number, firstArgument), $"{call} on x86-64");
    }

    [Theory]
    [InlineData("read", 63u, 0ul, ALLOWED)]
    [InlineData("socket", 198u, 0ul, REFUSED)]
    [InlineData("io_uring_setup", 425u, 0ul, REFUSED)]
    [InlineData("ptrace", 117u, 0ul, REFUSED)]
    [InlineData("process_vm_readv", 270u, 0ul, REFUSED)]
    [InlineData("process_vm_writev", 271u, 0ul, REFUSED)]
    [InlineData("pidfd_getfd", 438u, 0ul, REFUSED)]
    [InlineData("keyctl", 219u, 0ul, REFUSED)]
    [InlineData("clone of a thread", 220u, THREAD_FLAGS, ALLOWED)]
    [InlineData("clone of a process", 220u, PROCESS_FLAGS, REFUSED)]
    [InlineData("clone3", 435u, 0ul, NOT_IMPLEMENTED)]
    public void Arm64Filter_AnswersEachCall(string call, uint number, ulong firstArgument, uint answer)
    {
        Assert.True(answer == Run(Architecture.Arm64, ARM64, number, firstArgument), $"{call} on arm64");
    }

    [Theory]
    [InlineData(Architecture.X64, ARM64)]
    [InlineData(Architecture.Arm64, X64)]
    public void Filter_KillsACallOfAnotherArchitecture(Architecture filter, uint callArchitecture)
    {
        Assert.Equal(KILLED, Run(filter, callArchitecture, 0, 0));
    }

    /// <summary>Runs the filter over the seccomp_data of one call: its number, its architecture and its first argument.</summary>
    private static uint Run(Architecture architecture, uint callArchitecture, uint number, ulong firstArgument)
    {
        SeccompFilter.SockFilter[] filter = SeccompFilter.For(SeccompFilter.RulesFor(architecture));
        byte[] call = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(call.AsSpan(0), number);
        BinaryPrimitives.WriteUInt32LittleEndian(call.AsSpan(4), callArchitecture);
        BinaryPrimitives.WriteUInt64LittleEndian(call.AsSpan(16), firstArgument);

        uint accumulator = 0;
        for (int counter = 0; counter < filter.Length; counter++)
        {
            SeccompFilter.SockFilter instruction = filter[counter];
            switch (instruction.Code)
            {
                case LOAD_WORD:
                    accumulator = BinaryPrimitives.ReadUInt32LittleEndian(call.AsSpan((int)instruction.Value));
                    break;
                case JUMP_IF_EQUAL:
                    counter += accumulator == instruction.Value ? instruction.JumpIfTrue : instruction.JumpIfFalse;
                    break;
                case JUMP_IF_AT_LEAST:
                    counter += accumulator >= instruction.Value ? instruction.JumpIfTrue : instruction.JumpIfFalse;
                    break;
                case JUMP_IF_ANY_BIT:
                    counter += (accumulator & instruction.Value) != 0 ? instruction.JumpIfTrue : instruction.JumpIfFalse;
                    break;
                case RETURN:
                    return instruction.Value;
                default:
                    throw new InvalidOperationException($"The filter uses instruction {instruction.Code:x}, which this interpreter does not know.");
            }
        }

        throw new InvalidOperationException("The filter ended without an answer.");
    }
}
