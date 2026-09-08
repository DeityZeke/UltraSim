using System.Runtime.InteropServices;

namespace UltraSim.Network;

/// <summary>
/// Narrow Linux multi-datagram submission used only beneath the shared UDP
/// listener. The protocol and endpoint contracts remain platform-neutral.
/// </summary>
internal static unsafe class LinuxUdpBatch
{
    private const string Libc = "libc";
    private const int MessageTruncated = 0x20;
    private static readonly bool s_isSupported = DetectSupport("sendmmsg");
    private static readonly bool s_isReceiveSupported = DetectSupport("recvmmsg");

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoVector
    {
        internal void* Base;
        internal nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageHeader
    {
        internal void* Name;
        internal uint NameLength;
        internal IoVector* Vectors;
        internal nuint VectorCount;
        internal void* Control;
        internal nuint ControlLength;
        internal int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MultipleMessageHeader
    {
        internal MessageHeader Header;
        internal uint Length;
    }

    internal static bool IsSupported => s_isSupported;
    internal static bool IsReceiveSupported => s_isReceiveSupported;

    internal static int Send(
        int socketDescriptor,
        byte[] payloads,
        int maximumDatagramBytes,
        byte[] addresses,
        int addressBytes,
        int[] lengths,
        int head,
        int capacity,
        int count,
        out int error)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Linux sendmmsg is unavailable.");
        if (count <= 0 || count > 256) throw new ArgumentOutOfRangeException(nameof(count));

        IoVector* vectors = stackalloc IoVector[count];
        MultipleMessageHeader* messages = stackalloc MultipleMessageHeader[count];
        new Span<IoVector>(vectors, count).Clear();
        new Span<MultipleMessageHeader>(messages, count).Clear();

        fixed (byte* payloadBase = payloads)
        fixed (byte* addressBase = addresses)
        {
            for (int offset = 0; offset < count; offset++)
            {
                int slot = (head + offset) % capacity;
                vectors[offset].Base = payloadBase + checked(slot * maximumDatagramBytes);
                vectors[offset].Length = checked((nuint)lengths[slot]);
                messages[offset].Header.Name = addressBase + checked(slot * addressBytes);
                messages[offset].Header.NameLength = checked((uint)addressBytes);
                messages[offset].Header.Vectors = &vectors[offset];
                messages[offset].Header.VectorCount = 1;
            }

            int sent = SendMultiple(socketDescriptor, messages, checked((uint)count), 0);
            error = sent < 0 ? Marshal.GetLastPInvokeError() : 0;
            return sent;
        }
    }

    internal static int Receive(
        int socketDescriptor,
        byte[] payloads,
        int maximumDatagramBytes,
        byte[] addresses,
        int addressBytes,
        int[] lengths,
        int count,
        out int error)
    {
        if (!IsReceiveSupported)
            throw new PlatformNotSupportedException("Linux recvmmsg is unavailable.");
        if (count <= 0 || count > 256) throw new ArgumentOutOfRangeException(nameof(count));

        IoVector* vectors = stackalloc IoVector[count];
        MultipleMessageHeader* messages = stackalloc MultipleMessageHeader[count];
        new Span<IoVector>(vectors, count).Clear();
        new Span<MultipleMessageHeader>(messages, count).Clear();

        fixed (byte* payloadBase = payloads)
        fixed (byte* addressBase = addresses)
        {
            for (int index = 0; index < count; index++)
            {
                vectors[index].Base = payloadBase + checked(index * maximumDatagramBytes);
                vectors[index].Length = checked((nuint)maximumDatagramBytes);
                messages[index].Header.Name = addressBase + checked(index * addressBytes);
                messages[index].Header.NameLength = checked((uint)addressBytes);
                messages[index].Header.Vectors = &vectors[index];
                messages[index].Header.VectorCount = 1;
            }

            int received = ReceiveMultiple(
                socketDescriptor, messages, checked((uint)count), 0, null);
            error = received < 0 ? Marshal.GetLastPInvokeError() : 0;
            for (int index = 0; index < received; index++)
                lengths[index] = (messages[index].Header.Flags & MessageTruncated) != 0
                    ? -1
                    : checked((int)messages[index].Length);
            return received;
        }
    }

    [DllImport(Libc, EntryPoint = "sendmmsg", SetLastError = true)]
    private static extern int SendMultiple(
        int socket,
        MultipleMessageHeader* messages,
        uint count,
        int flags);

    [DllImport(Libc, EntryPoint = "recvmmsg", SetLastError = true)]
    private static extern int ReceiveMultiple(
        int socket,
        MultipleMessageHeader* messages,
        uint count,
        int flags,
        void* timeout);

    private static bool DetectSupport(string export)
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ||
            IntPtr.Size != 8 || sizeof(IoVector) != 16 ||
            sizeof(MessageHeader) != 56 || sizeof(MultipleMessageHeader) != 64)
            return false;
        if (!NativeLibrary.TryLoad(Libc, out nint library)) return false;
        try
        {
            return NativeLibrary.TryGetExport(library, export, out _);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}
