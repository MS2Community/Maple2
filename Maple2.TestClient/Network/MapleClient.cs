using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Serilog;

namespace Maple2.TestClient.Network;

/// <summary>
/// Low-level TCP client that handles the Maple handshake, MapleCipher encryption/decryption,
/// and packet send/receive with dispatch by SendOp.
/// </summary>
public class MapleClient : IDisposable {
    private const int HANDSHAKE_HEADER_SIZE = 6; // WriteHeader prepends a 6-byte header for unencrypted packets

    private static readonly ILogger Logger = Log.Logger.ForContext<MapleClient>();

    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private MapleCipher.Encryptor? sendCipher;
    private MapleCipher.Decryptor? recvCipher;
    private Thread? recvThread;
    private volatile bool disposed;

    // One-shot waiters: first packet matching the opcode completes the TCS
    private readonly ConcurrentDictionary<SendOp, ConcurrentQueue<TaskCompletionSource<byte[]>>> waiters = new();

    // Persistent handlers
    private readonly ConcurrentDictionary<SendOp, Action<byte[]>> handlers = new();

    // All received packets (for debugging)
    public event Action<SendOp, byte[]>? OnPacketReceived;

    /// <summary>
    /// Connect to the server, read the handshake, and initialize ciphers.
    /// </summary>
    public async Task ConnectAsync(string host, int port) {
        tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(host, port);
        stream = tcpClient.GetStream();

        // Server sends handshake as: [header(6 bytes)][payload(19 bytes)]
        // The header written by WriteHeader for unencrypted packets is:
        //   sequenceId(2) + packetLength(4) — total 6 bytes
        // Payload: SendOp(2) + version(4) + riv(4) + siv(4) + blockIV(4) + patchType(1) = 19 bytes
        byte[] headerBuf = new byte[HANDSHAKE_HEADER_SIZE];
        await ReadExactAsync(stream, headerBuf, HANDSHAKE_HEADER_SIZE);

        int payloadLength = BitConverter.ToInt32(headerBuf, 2);
        byte[] payload = new byte[payloadLength];
        await ReadExactAsync(stream, payload, payloadLength);

        var reader = new ByteReader(payload, 0);
        var opcode = reader.Read<SendOp>();
        if (opcode != SendOp.RequestVersion) {
            throw new InvalidOperationException($"Expected RequestVersion handshake, got {opcode}");
        }

        uint version = reader.Read<uint>();
        uint serverRiv = reader.Read<uint>(); // server's recv IV = our send IV
        uint serverSiv = reader.Read<uint>(); // server's send IV = our recv IV
        uint blockIv = reader.Read<uint>();
        byte patchType = reader.ReadByte();

        if (version != Session.VERSION) {
            throw new InvalidOperationException($"Version mismatch: server={version}, expected={Session.VERSION}");
        }

        // Client sends with server's RIV, client receives with server's SIV
        sendCipher = new MapleCipher.Encryptor(Session.VERSION, serverRiv, blockIv);
        recvCipher = new MapleCipher.Decryptor(Session.VERSION, serverSiv, blockIv);

        // IMPORTANT: Server's sendCipher.WriteHeader() advanced its IV once during handshake.
        // We must advance recvCipher's IV to stay in sync by feeding the raw handshake through TryDecrypt.
        byte[] rawHandshake = new byte[HANDSHAKE_HEADER_SIZE + payloadLength];
        Buffer.BlockCopy(headerBuf, 0, rawHandshake, 0, HANDSHAKE_HEADER_SIZE);
        Buffer.BlockCopy(payload, 0, rawHandshake, HANDSHAKE_HEADER_SIZE, payloadLength);
        recvCipher.TryDecrypt(new ReadOnlySequence<byte>(rawHandshake), out PoolByteReader handshakeReader);
        handshakeReader.Dispose();

        Logger.Information("Connected to {Host}:{Port} (version={Version}, patchType={PatchType})", host, port, version, patchType);

        // Start background receive loop
        recvThread = new Thread(ReceiveLoop) {
            Name = $"MapleClient-Recv-{host}:{port}",
            IsBackground = true,
        };
        recvThread.Start();
    }

    /// <summary>
    /// Send a packet to the server (encrypts automatically).
    /// The packet buffer should start with RecvOp (2 bytes).
    /// </summary>
    public void Send(ByteWriter packet) {
        if (disposed || sendCipher == null || stream == null) {
            throw new InvalidOperationException("Not connected");
        }

        lock (sendCipher) {
            using PoolByteWriter encrypted = sendCipher.Encrypt(packet.Buffer, 0, packet.Length);
            stream.Write(encrypted.Buffer, 0, encrypted.Length);
        }
    }

    /// <summary>
    /// Wait for a single packet with the given opcode. Returns the raw decrypted payload (including opcode).
    /// </summary>
    public Task<byte[]> WaitForPacketAsync(SendOp opcode, TimeSpan? timeout = null) {
        timeout ??= TimeSpan.FromSeconds(10);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var queue = waiters.GetOrAdd(opcode, _ => new ConcurrentQueue<TaskCompletionSource<byte[]>>());
        queue.Enqueue(tcs);

        // Timeout cancellation
        var cts = new CancellationTokenSource(timeout.Value);
        cts.Token.Register(() => tcs.TrySetException(
            new TimeoutException($"Timed out waiting for {opcode} after {timeout.Value.TotalSeconds}s")));

        // Dispose the CTS when the task completes regardless of outcome
        tcs.Task.ContinueWith(_ => cts.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
        return tcs.Task;
    }

    /// <summary>
    /// Register a persistent handler for a given opcode.
    /// </summary>
    public void On(SendOp opcode, Action<byte[]> handler) {
        handlers[opcode] = handler;
    }

    public void Disconnect() {
        Dispose();
    }

    private void ReceiveLoop() {
        try {
            var buffer = new byte[4096];
            var accumulator = new MemoryStream();

            while (!disposed && stream != null) {
                int bytesRead;
                try {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                } catch (Exception) when (disposed) {
                    break;
                }

                if (bytesRead <= 0) {
                    Logger.Debug("ReceiveLoop: connection closed (bytesRead=0)");
                    break;
                }

                Logger.Debug("ReceiveLoop: received {Bytes} bytes", bytesRead);
                accumulator.Write(buffer, 0, bytesRead);

                // Try to decrypt complete packets from the accumulated buffer
                ProcessAccumulatedData(accumulator);
            }
        } catch (Exception ex) when (!disposed) {
            Logger.Error(ex, "ReceiveLoop error");
        }
    }

    private void ProcessAccumulatedData(MemoryStream accumulator) {
        if (recvCipher == null) return;

        byte[] data = accumulator.ToArray();
        var sequence = new ReadOnlySequence<byte>(data);

        int totalConsumed = 0;
        while (true) {
            int bytesConsumed = recvCipher.TryDecrypt(sequence, out PoolByteReader packet);
            if (bytesConsumed <= 0) break;

            try {
                byte[] raw = new byte[packet.Length];
                Array.Copy(packet.Buffer, 0, raw, 0, packet.Length);
                DispatchPacket(raw);
            } finally {
                packet.Dispose();
            }

            totalConsumed += bytesConsumed;
            sequence = sequence.Slice(bytesConsumed);
        }

        if (totalConsumed > 0) {
            // Remove consumed bytes from accumulator
            byte[] remaining = data.AsSpan(totalConsumed).ToArray();
            accumulator.SetLength(0);
            accumulator.Write(remaining, 0, remaining.Length);
        }
    }

    private void DispatchPacket(byte[] raw) {
        if (raw.Length < 2) return;

        var opcode = (SendOp) (raw[1] << 8 | raw[0]);
        Logger.Debug("Dispatching packet: {Opcode} (0x{Code:X4}), length={Length}", opcode, (ushort) opcode, raw.Length);
        OnPacketReceived?.Invoke(opcode, raw);

        // Check one-shot waiters first
        if (waiters.TryGetValue(opcode, out var queue)) {
            while (queue.TryDequeue(out var tcs)) {
                if (tcs.TrySetResult(raw)) {
                    return; // Only one waiter gets this packet
                }
            }
        }

        // Then persistent handlers
        if (handlers.TryGetValue(opcode, out var handler)) {
            try {
                handler(raw);
            } catch (Exception ex) {
                Logger.Error(ex, "Handler error for {Opcode}", opcode);
            }
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count) {
        int offset = 0;
        while (offset < count) {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read <= 0) throw new EndOfStreamException("Connection closed during read");
            offset += read;
        }
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;

        try { stream?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }
        recvThread?.Join(2000);

        // Cancel all pending waiters
        foreach (var (_, queue) in waiters) {
            while (queue.TryDequeue(out var tcs)) {
                tcs.TrySetCanceled();
            }
        }
    }
}
