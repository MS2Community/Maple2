using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Maple2.Model.Enum;
using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Core.Network;

public enum PatchType : byte {
    Delete = 0,
    Patch = 1,
    Ignore = 2,
}

public abstract class Session : IDisposable {
    public const uint VERSION = 12;
    private const uint BLOCK_IV = 12; // TODO: should this be variable

    private const int HANDSHAKE_SIZE = 19;
    private const int STOP_TIMEOUT = 2000;

    // Send timeout to prevent indefinite blocking (in milliseconds)
    private const int SEND_TIMEOUT_MS = 5000;

    public SessionState State { get; set; }

    public EventHandler<string>? OnError;
    public EventHandler<IByteReader>? OnPacket;
    public Action? OnLoop;

    private bool disposed;
    private int disconnecting; // 0 = not disconnecting, 1 = disconnect in progress/already triggered (reentrancy guard)
    private readonly uint siv;
    private readonly uint riv;

    private readonly string name;
    private readonly TcpClient client;
    private readonly NetworkStream networkStream;
    private readonly MapleCipher.Encryptor sendCipher;
    private readonly MapleCipher.Decryptor recvCipher;

    private readonly Thread thread;
    private readonly QueuedPipeScheduler pipeScheduler;
    private readonly Pipe recvPipe;

    // Send queue for non-blocking sends
    private readonly BlockingCollection<(byte[] packet, int length)> sendQueue = new(new ConcurrentQueue<(byte[], int)>());
    private Thread sendWorkerThread = null!;

    public long AccountId { get; protected set; }
    public long CharacterId { get; protected set; }
    private readonly ConcurrentDictionary<SendOp, byte[]> lastSentPackets = [];

    protected abstract PatchType Type { get; }
    protected readonly ILogger Logger = Log.Logger.ForContext<Session>();

    protected Session(TcpClient tcpClient) {
        thread = new Thread(StartInternal);
        pipeScheduler = new QueuedPipeScheduler();
        var options = new PipeOptions(
            readerScheduler: pipeScheduler,
            writerScheduler: pipeScheduler,
            useSynchronizationContext: false
        );
        recvPipe = new Pipe(options);

        // Allow client to close immediately
        tcpClient.LingerState = new LingerOption(true, 0);
        name = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

        byte[] sivBytes = RandomNumberGenerator.GetBytes(4);
        byte[] rivBytes = RandomNumberGenerator.GetBytes(4);
        siv = BitConverter.ToUInt32(sivBytes);
        riv = BitConverter.ToUInt32(rivBytes);

        client = tcpClient;
        networkStream = tcpClient.GetStream();
        sendCipher = new MapleCipher.Encryptor(VERSION, siv, BLOCK_IV);
        recvCipher = new MapleCipher.Decryptor(VERSION, riv, BLOCK_IV);

        // Start send worker thread
        sendWorkerThread = new Thread(SendWorker) {
            Name = $"SendWorker-{name}",
            IsBackground = true,
        };
        sendWorkerThread.Start();
    }

    ~Session() => Dispose(false);

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposed) return;

        disposed = true;
        State = SessionState.Disconnected;
        try {
            Complete();
        } catch (Exception ex) {
            Logger.Debug(ex, "Complete() threw during Dispose");
        }
        try {
            thread.Join(STOP_TIMEOUT);
        } catch (Exception ex) {
            Logger.Debug(ex, "thread.Join failed");
        }
        try {
            CloseClient();
        } catch (Exception ex) {
            Logger.Debug(ex, "CloseClient failed");
        }
    }

    protected void Complete() {
        recvPipe.Writer.Complete();
        recvPipe.Reader.Complete();
        pipeScheduler.Complete();
        sendQueue.CompleteAdding();
    }

    public void Disconnect([CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string filePath = "") {
        if (disposed) return;

        if (Interlocked.Exchange(ref disconnecting, 1) == 1) return;
        Logger.Information("Disconnected {Session} at {Caller} in {FilePath} on line {LineNumber}", this, caller, filePath, line);

        // Drain the send queue before disposing — ensures queued packets (e.g. migration)
        // are delivered to the client before the connection is closed.
        try { sendQueue.CompleteAdding(); } catch (ObjectDisposedException) { }
        try { sendWorkerThread.Join(STOP_TIMEOUT); } catch (Exception ex) {
            Logger.Debug(ex, "SendWorker drain join failed");
        }

        Dispose();
    }

    public bool Connected() {
        if (disposed) {
            return false;
        }

        Socket socket = client.Client;
        return !((socket.Poll(1000, SelectMode.SelectRead) && (socket.Available == 0)) || !socket.Connected);
    }

    public void Start() {
        if (disposed) {
            throw new ObjectDisposedException("Session has been disposed.");
        }
        if (client == null) {
            throw new InvalidOperationException("Cannot start a session without a client.");
        }

        thread.Start();
    }

    public void Send(params byte[] packet) => SendInternal(packet, packet.Length);

    public void Send(ByteWriter packet) => SendInternal(packet.Buffer, packet.Length);

    public override string ToString() => $"{GetType().Name} from {name}";


    private void StartInternal() {
        try {
            PerformHandshake(); // Perform handshake to initialize connection

            // Pipeline tasks can be run asynchronously
            Task writeTask = WriteRecvPipe(client.Client, recvPipe.Writer);
            Task readTask = ReadRecvPipe(recvPipe.Reader);
            Task.WhenAll(writeTask, readTask).ContinueWith(t => {
                if (t.IsFaulted) {
                    Logger.Debug(t.Exception, "Pipeline aggregate fault account={AccountId} char={CharacterId}", AccountId, CharacterId);
                } else if (t.IsCanceled) {
                    Logger.Debug("Pipeline tasks cancelled account={AccountId} char={CharacterId}", AccountId, CharacterId);
                }
                CloseClient();
            });

            while (!disposed && pipeScheduler.OutputAvailableAsync().Result) {
                pipeScheduler.ProcessQueue();
                OnLoop?.Invoke();
            }
        } catch (Exception ex) {
            if (!disposed) {
                Logger.Error(ex, "Exception on session thread");
            }
        } finally { Disconnect(); }
    }

    private void PerformHandshake() {
        var handshake = new ByteWriter(HANDSHAKE_SIZE);
        handshake.Write<SendOp>(SendOp.RequestVersion);
        handshake.Write<uint>(VERSION);
        handshake.Write<uint>(riv);
        handshake.Write<uint>(siv);
        handshake.Write<uint>(BLOCK_IV);
        handshake.WriteByte((byte) Type);

        // No encryption for handshake
        using PoolByteWriter packet = sendCipher.WriteHeader(handshake.Buffer, 0, handshake.Length);
        SendRaw(packet);
    }

    private async Task WriteRecvPipe(Socket socket, PipeWriter writer) {
        try {
            FlushResult result = default;
            do {
                if (disposed) break;

                Memory<byte> memory = writer.GetMemory();
                int bytesRead;

                try {
                    bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);
                } catch (SocketException sockEx) when (sockEx.ErrorCode == 995 || sockEx.ErrorCode == 10004) {
                    // 995: Operation aborted (thread exit/app request)
                    // 10004: Interrupted system call
                    // These are expected when closing the session
                    Logger.Debug("Socket closed during receive (code {ErrorCode}) account={AccountId} char={CharacterId}",
                        sockEx.ErrorCode, AccountId, CharacterId);
                    break;
                }

                if (bytesRead <= 0 || disposed) {
                    break;
                }

                // Check if writer was completed before advancing
                try {
                    writer.Advance(bytesRead);
                    result = await writer.FlushAsync();
                } catch (InvalidOperationException) when (disposed) {
                    // Writer was completed/disposed during advance or flush
                    Logger.Debug("Pipe writer completed during operation account={AccountId} char={CharacterId}", AccountId, CharacterId);
                    break;
                } catch (ArgumentOutOfRangeException) when (disposed) {
                    // Invalid byte count during disposal
                    Logger.Debug("Pipe writer advance failed during disposal account={AccountId} char={CharacterId}", AccountId, CharacterId);
                    break;
                }
            } while (!disposed && !result.IsCompleted);
        } catch (Exception ex) when (disposed) {
            // Suppress exceptions if we're already disposed
            Logger.Debug(ex, "WriteRecvPipe exception during disposal account={AccountId} char={CharacterId}", AccountId, CharacterId);
        } catch (Exception ex) {
            Logger.Debug(ex, "WriteRecvPipe exception account={AccountId} char={CharacterId}", AccountId, CharacterId);
            Disconnect();
        }
    }

    private async Task ReadRecvPipe(PipeReader reader) {
        try {
            ReadResult result;
            do {
                result = await reader.ReadAsync();

                int bytesRead;
                ReadOnlySequence<byte> buffer = result.Buffer;
                while ((bytesRead = recvCipher.TryDecrypt(buffer, out PoolByteReader packet)) > 0) {
                    try {
#if DEBUG
                        LogRecv(packet.Buffer);
#endif
                        OnPacket?.Invoke(this, packet); // handle packet
                    } finally {
                        packet.Dispose();
                    }
                    buffer = buffer.Slice(bytesRead);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            } while (!disposed && !result.IsCompleted);
        } catch (Exception ex) {
            // Stop web crawlers
            if (ex.Message.StartsWith("Packet has invalid sequence header")) {
                return;
            }

            if (ex is InvalidOperationException invalidOperation && invalidOperation.Message.Contains("reader was completed")) {
                // Ignore this exception, it happens when the reader is completed, either by the client closing or by the session being disposed.
                // it's not a real error
                return;
            }
            if (!disposed) {
                Logger.Error(ex, "Exception reading recv packet || AccountId: {AccountId}, CharacterId: {CharacterId}", AccountId, CharacterId);
            }
        } finally { Disconnect(); }
    }


    /*
     * Send packet to client
     * packet: packet to send
     * length: length of packet that only includes data
     */
    private void SendInternal(byte[] packet, int length) {
        if (disposed || disconnecting == 1) return;
#if DEBUG
        LogSend(packet, length);
#endif
        // Track last sent packet by opcode
        if (length >= 2) {
            var op = (SendOp) (packet[1] << 8 | packet[0]);
            // Store a copy to avoid mutation issues
            lastSentPackets[op] = packet.Take(length).ToArray();
        }

        // Queue the raw packet for background processing
        // Make a copy since the caller may reuse the buffer
        byte[] packetCopy = packet.Take(length).ToArray();
        try {
            sendQueue.Add((packetCopy, length));
        } catch (InvalidOperationException) {
            // Queue was completed/disposed
            Logger.Debug("SendQueue add failed - queue completed");
        }
    }

    private void SendRaw(ByteWriter packet) {
        if (disposed) return;

        try {
            // Use async write with timeout to prevent indefinite blocking
            Task writeTask = networkStream.WriteAsync(packet.Buffer, 0, packet.Length);
            if (!writeTask.Wait(SEND_TIMEOUT_MS)) {
                Logger.Warning("SendRaw timeout after {Timeout}ms, disconnecting account={AccountId} char={CharacterId}",
                    SEND_TIMEOUT_MS, AccountId, CharacterId);

                // Observe the task exception to prevent unobserved task exception
                // when the task eventually completes/faults after timeout
                _ = writeTask.ContinueWith(t => {
                    if (t.IsFaulted) {
                        Logger.Debug(t.Exception, "WriteAsync faulted after timeout account={AccountId} char={CharacterId}",
                            AccountId, CharacterId);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);

                Disconnect();
                return;
            }

            // Check if write actually failed
            if (writeTask.IsFaulted) {
                throw writeTask.Exception?.GetBaseException() ?? new Exception("Write task faulted");
            }
        } catch (Exception ex) when (ex.InnerException is IOException or SocketException || ex is IOException or SocketException) {
            // Expected when client closes the connection (e.g., during migration)
            Logger.Debug("SendRaw connection closed account={AccountId} char={CharacterId}", AccountId, CharacterId);
            Disconnect();
        } catch (Exception ex) {
            Logger.Warning(ex, "[LIFECYCLE] SendRaw write failed account={AccountId} char={CharacterId}", AccountId, CharacterId);
            Disconnect();
        }
    }

    private void SendWorker() {
        try {
            foreach ((byte[] packet, int length) in sendQueue.GetConsumingEnumerable()) {
                if (disposed) break;

                // Encrypt outside lock, then send with timeout
                PoolByteWriter encryptedPacket;
                lock (sendCipher) {
                    if (disposed) break;
                    encryptedPacket = sendCipher.Encrypt(packet, 0, length);
                }
                try {
                    SendRaw(encryptedPacket);
                } finally {
                    encryptedPacket.Dispose();
                }
            }
        } catch (Exception ex) {
            if (!disposed) {
                Logger.Error(ex, "SendWorker exception account={AccountId} char={CharacterId}", AccountId, CharacterId);
            }
        }
    }

    public byte[]? GetLastSentPacket(SendOp op) {
        lastSentPackets.TryGetValue(op, out byte[]? packet);
        return packet;
    }

    private void CloseClient() {
        // Must close socket before network stream to prevent lingering
        client.Client?.Close();
        client.Close();
    }

    private void LogSend(byte[] packet, int length) {
        short op = (short) (packet[1] << 8 | packet[0]);
        SendOp opcode = (SendOp) op;
        switch (opcode) {
            case SendOp.UserSync:
            case SendOp.NpcControl:
            case SendOp.ProxyGameObj:
            case SendOp.ResponseTimeSync:
            case SendOp.Stat:
            case SendOp.RequestHeartbeat:
            case SendOp.FurnishingInventory:
            case SendOp.FurnishingStorage:
            case SendOp.Vibrate:
            case SendOp.Insignia:
                break;
            default:
                Logger.Verbose("{Mode} ({Name} - {OpCode}): {Packet}", "SEND".ColorRed(), opcode, $"0x{op:X4}", packet.ToHexString(length, ' '));
                break;
        }
    }

    private void LogRecv(byte[] packet) {
        short op = (short) (packet[1] << 8 | packet[0]);
        RecvOp opcode = (RecvOp) op;
        switch (opcode) {
            case RecvOp.UserSync:
            case RecvOp.RequestTimeSync:
            case RecvOp.GuideObjectSync:
            case RecvOp.RideSync:
            case RecvOp.ResponseHeartbeat:
            case RecvOp.Insignia:
                break;
            default:
                Logger.Verbose("{Mode} ({Name} - {OpCode}): {Packet}", "RECV".ColorGreen(), opcode, $"0x{op:X4}", packet.ToHexString(packet.Length, ' '));
                break;
        }
    }

    public string ExtractIp() {
        int lastColon = name.LastIndexOf(':');
        if (name.StartsWith('[') && name.Contains(']')) {
            int endBracket = name.IndexOf(']');
            if (endBracket > 1) {
                return name.Substring(1, endBracket - 1);
            }
        }
        if (lastColon > 0) {
            return name[..lastColon];
        }
        return name;
    }
}
