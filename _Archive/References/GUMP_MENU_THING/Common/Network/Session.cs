
#nullable enable

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

using Godot;

namespace Common.Network
{
    public delegate void PacketEventHandler(Session session, Packet packet);

    public class Session : IDisposable
    {
        public Socket Socket { get; }
        public bool IsThrottled { get; set; }
        public bool IsDisposed { get; private set; }

        // Incoming framing buffer (sliding window)
        //private readonly byte[] m_RecvBuffer = BufferPool.Rent(8192);
        private int m_ReadStart = 0, m_ReadEnd = 0;

        // Outbound queue of length-prefixed buffers
        private readonly ConcurrentQueue<ArraySegment<byte>> m_SendQueue = new();
        private readonly ConcurrentQueue<Packet> m_Inbound = new();
        private readonly SocketAsyncEventArgs m_WriteArgs = new SocketAsyncEventArgs();
        private bool m_WritePending = false;

        // One SAEA for reads
        private readonly SocketAsyncEventArgs m_ReadArgs = new SocketAsyncEventArgs();

        public event PacketEventHandler? OnPacketReceive;
        public event Action? OnThrottled;
        public event Action? OnDisconnected;

        public Session(Socket socket)
        {
            Socket = socket;

            // Setup receive args
            //m_ReadArgs.SetBuffer(m_RecvBuffer, 0, m_RecvBuffer.Length);
            m_ReadArgs.Completed += IO_Completed;

            // Setup write args; buffer set per-send
            m_WriteArgs.Completed += IO_Completed;
        }

        public void Start()
        {
            /*
            m_ReadArgs.SetBuffer(
            m_RecvBuffer,
            m_ReadEnd,
            m_RecvBuffer.Length - m_ReadEnd
            );
            */
            m_ReadArgs.SetBuffer(m_recvTemp, 0, m_recvTemp.Length);

            // Kick off the first receive operation
            if (!Socket.ReceiveAsync(m_ReadArgs))
                ProcessReceive(m_ReadArgs);
        }

        // Unified callback for both read & write completions
        private void IO_Completed(object? sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected socket op: " + e.LastOperation);
            }
        }

        public bool TryDequeueInbound(out Packet packet) => m_Inbound.TryDequeue(out packet!);

        private readonly ByteRingBuffer m_Ring = new(8192);
        private readonly byte[] m_recvTemp = BufferPool.Rent(4096);//new byte[4096];

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
            {
                Dispose();
                return;
            }

            // 1) Copy received bytes into sliding window
            //Buffer.BlockCopy(m_RecvBuffer, 0, m_RecvBuffer, m_ReadEnd, e.BytesTransferred);
            //m_ReadEnd += e.BytesTransferred;
            m_Ring.Write(new ReadOnlySpan<byte>(m_recvTemp, 0, e.BytesTransferred));

            // 2) Frame and dispatch packets
            ProcessFramedPackets();

            //e.SetBuffer(m_RecvBuffer, m_ReadEnd, m_RecvBuffer.Length - m_ReadEnd);

            // 3) Re-arm receive
            if (!Socket.ReceiveAsync(e))
                ProcessReceive(e);
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            // Return just-sent buffer to pool
            if (e.UserToken is ArraySegment<byte> segment)
                BufferPool.Return(segment.Array!);

            m_WritePending = false;
            // Send next if any
            StartSend();
        }

        public void QueueSend(Packet packet)
        {
            // --- Serialize and measure payload size without GetPayloadSize() ---
            byte[] buffer = BufferPool.Rent(2048);
            var writer = new BinaryWriter(buffer.AsSpan(0, buffer.Length));
            writer.WriteUShort(0); // Placeholder for length (2 bytes)
            writer.WriteByte(packet.PacketID);

            int payloadStart = writer.BytesWritten;

            packet.Serialize(ref writer);
            //packet.Serialize(ref buffer.AsSpan(writer.BytesWritten, buffer.Length - writer.BytesWritten));

            int payloadLen = writer.BytesWritten - payloadStart;

            // Write the actual length (payloadLen + 1 for packetID)
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 2), (ushort)(1 + payloadLen));
            int totalLen = 2 + 1 + payloadLen;

            GD.Print($"[NETWORK] Send Packet: [{packet.GetType().Name}] ");
            GD.Print($"[NETWORK] Send Packet (RAW): [{string.Join(", ", buffer.AsSpan(0, writer.BytesWritten).ToArray())}]");
            string hex = BitConverter.ToString(buffer, 0, writer.BytesWritten).Replace("-", " ");
            GD.Print($"[NETWORK] Send Packet (HEX): [{hex}]");

            m_SendQueue.Enqueue(new ArraySegment<byte>(buffer, 0, totalLen));
            StartSend();
        }

        private void StartSend()
        {
            // Only one pending at a time
            if (m_WritePending) return;

            if (m_SendQueue.TryDequeue(out var seg))
            {
                m_WritePending = true;
                m_WriteArgs.SetBuffer(seg.Array, seg.Offset, seg.Count);
                m_WriteArgs.UserToken = seg;

                if (!Socket.SendAsync(m_WriteArgs))
                    ProcessSend(m_WriteArgs);
            }
        }

        // TCP sliding window framing logic
        private void ProcessFramedPackets()
        {
            if (m_Ring.Count < 2)
                return;

            while (m_Ring.Count >= 3)
            {
                // peek length prefix
                Span<byte> header = m_Ring.Peek(2);
                ushort packetLen = BitConverter.ToUInt16(header);

                if (packetLen == 0)
                {
                    // ghost—drop the two bytes
                    m_Ring.Advance(2);
                    continue;
                }

                // if (packetLen > m_Ring.Count - 2)
                //     break; // wait for more

                if (m_Ring.Count < 2 + packetLen)
                    break;

                // we have a full record: length+payload
                // peek the full payload
                var payload = new byte[packetLen];
                // skip length
                m_Ring.Advance(2);
                // read packetID + body
                m_Ring.Read(payload);

                GD.Print($"[RING] Read {packetLen} bytes: {BitConverter.ToString(payload)}");

                // hand off to parser
                var packet = PacketManager.Parse(payload.AsSpan());

                if (packet != null)
                {
                    OnPacketReceive?.Invoke(this, packet);
                    m_Inbound.Enqueue(packet);
                }
            }


            /*
                int offset = m_ReadStart;
                while (true)
                {
                    if (m_ReadEnd - offset < 3) break; // Not enough for header

                    ushort packetLen = BitConverter.ToUInt16(m_RecvBuffer, offset);
                    if (packetLen == 0)
                    {
                        offset += 2;
                        continue;
                    }
                    if (packetLen > m_RecvBuffer.Length)
                    {
                        offset = m_ReadEnd = 0;
                        break;
                    }
                    if (m_ReadEnd - offset - 2 < packetLen) break; // Need more data

                    var packetSpan = new ReadOnlySpan<byte>(m_RecvBuffer, offset + 2, packetLen); // Strip the Length of the packet, leave ID + Payload
                    var packet = PacketManager.Parse(packetSpan);
                    if (packet != null)
                    {
                        OnPacketReceive?.Invoke(this, packet);
                        m_Inbound.Enqueue(packet);
                    }

                    offset += packetLen + 2;
                }
                int rem = m_ReadEnd - offset;
                if (rem > 0)
                    Buffer.BlockCopy(m_RecvBuffer, offset, m_RecvBuffer, 0, rem);
                m_ReadStart = 0;
                m_ReadEnd = rem;
                */
            }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            try { Socket.Shutdown(SocketShutdown.Both); } catch { }
            Socket.Close();
            m_ReadArgs.Dispose();
            m_WriteArgs.Dispose();
            //BufferPool.Return(m_RecvBuffer);
            BufferPool.Return(m_recvTemp);
            // Drain send queue and return buffers
            while (m_SendQueue.TryDequeue(out var seg))
                BufferPool.Return(seg.Array!);
        }
    }
}