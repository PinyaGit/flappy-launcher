using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Lightweight, zero-dependency Discord Rich Presence client over local Named Pipes.
    /// Gracefully does nothing if Discord is not running or pipe fails.
    /// </summary>
    internal static class DiscordRpc
    {
        private const string DEFAULT_CLIENT_ID = "1225895781920706591"; // Flappy Discord Client ID
        private static NamedPipeClientStream _pipe;
        private static readonly object _lock = new object();
        private static bool _connected = false;
        private static long _startTime = 0;

        public static void Initialize()
        {
            if (_startTime == 0)
            {
                _startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            Task.Run(() => ConnectAsync());
        }

        private static void ConnectAsync()
        {
            lock (_lock)
            {
                if (_connected && _pipe != null && _pipe.IsConnected) return;

                ClosePipe();

                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                        pipe.Connect(100); // 100ms timeout
                        if (pipe.IsConnected)
                        {
                            _pipe = pipe;
                            _connected = true;

                            // Send handshake
                            string handshakeJson = "{\"v\":1,\"client_id\":\"" + DEFAULT_CLIENT_ID + "\"}";
                            SendPacket(0, handshakeJson);

                            // Read response in background to drain buffer
                            Task.Run(() => ReadLoop(_pipe));
                            break;
                        }
                    }
                    catch
                    {
                        // Try next pipe
                    }
                }
            }
        }

        private static void ReadLoop(NamedPipeClientStream pipe)
        {
            try
            {
                byte[] header = new byte[8];
                while (_connected && pipe.IsConnected)
                {
                    int read = pipe.Read(header, 0, 8);
                    if (read < 8) break;
                    int length = BitConverter.ToInt32(header, 4);
                    if (length > 0 && length < 65536)
                    {
                        byte[] body = new byte[length];
                        pipe.Read(body, 0, length);
                    }
                }
            }
            catch { }
            finally
            {
                _connected = false;
            }
        }

        private static void SendPacket(int opcode, string json)
        {
            if (_pipe == null || !_pipe.IsConnected) return;
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(json);
                byte[] header = new byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, header, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 4, 4);

                _pipe.Write(header, 0, 8);
                _pipe.Write(payload, 0, payload.Length);
                _pipe.Flush();
            }
            catch
            {
                _connected = false;
                ClosePipe();
            }
        }

        public static void UpdatePresence(string details, string state)
        {
            try
            {
                if (!_connected || _pipe == null || !_pipe.IsConnected)
                {
                    ConnectAsync();
                }

                if (!_connected) return;

                int pid = Process.GetCurrentProcess().Id;
                string nonce = Guid.NewGuid().ToString("N");

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"cmd\":\"SET_ACTIVITY\",");
                sb.Append("\"args\":{");
                sb.Append("\"pid\":").Append(pid).Append(",");
                sb.Append("\"activity\":{");
                sb.Append("\"details\":").Append(JsonEscape(details)).Append(",");
                sb.Append("\"state\":").Append(JsonEscape(state)).Append(",");
                sb.Append("\"timestamps\":{\"start\":").Append(_startTime).Append("},");
                sb.Append("\"assets\":{");
                sb.Append("\"large_image\":\"skyrim\",");
                sb.Append("\"large_text\":\"Flappy Modpack\"");
                sb.Append("}");
                sb.Append("}");
                sb.Append("},");
                sb.Append("\"nonce\":\"").Append(nonce).Append("\"");
                sb.Append("}");

                SendPacket(1, sb.ToString());
            }
            catch { }
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                try
                {
                    if (_connected && _pipe != null && _pipe.IsConnected)
                    {
                        SendPacket(2, "{}");
                    }
                }
                catch { }
                finally
                {
                    ClosePipe();
                }
            }
        }

        private static void ClosePipe()
        {
            _connected = false;
            try
            {
                if (_pipe != null)
                {
                    _pipe.Dispose();
                    _pipe = null;
                }
            }
            catch { }
        }
    }
}
