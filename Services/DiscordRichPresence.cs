using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RealmLauncher.Services
{
    public sealed class DiscordRichPresence : IDisposable
    {
        private const int OpHandshake = 0;
        private const int OpFrame = 1;
        private const int OpClose = 2;

        private readonly string _applicationId;
        private readonly object _sync = new object();
        private NamedPipeClientStream _pipe;
        private bool _connected;
        private bool _disposed;
        private int _nonce;

        public DiscordRichPresence(string applicationId)
        {
            _applicationId = applicationId;
        }

        public bool IsEnabled
        {
            get { return !string.IsNullOrWhiteSpace(_applicationId); }
        }

        public bool IsConnected
        {
            get { lock (_sync) { return _connected; } }
        }

        public Task<bool> ConnectAsync()
        {
            if (!IsEnabled)
            {
                return Task.FromResult(false);
            }

            return Task.Run(() =>
            {
                for (var i = 0; i < 10; i++)
                {
                    try
                    {
                        var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                        pipe.Connect(500);

                        lock (_sync)
                        {
                            _pipe = pipe;
                        }

                        WriteFrame(OpHandshake, JsonConvert.SerializeObject(new
                        {
                            v = 1,
                            client_id = _applicationId
                        }));

                        lock (_sync)
                        {
                            _connected = true;
                        }
                        return true;
                    }
                    catch
                    {
                    }
                }

                return false;
            });
        }

        public void SetPresence(string details, string state, string largeImageKey, string largeImageText, DateTime? startedUtc)
        {
            if (!IsConnected)
            {
                return;
            }

            try
            {
                object timestamps = null;
                if (startedUtc.HasValue)
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    timestamps = new { start = (long)(startedUtc.Value.ToUniversalTime() - epoch).TotalSeconds };
                }

                var payload = JsonConvert.SerializeObject(new
                {
                    cmd = "SET_ACTIVITY",
                    nonce = Interlocked.Increment(ref _nonce).ToString(),
                    args = new
                    {
                        pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                        activity = new
                        {
                            details,
                            state,
                            timestamps,
                            assets = string.IsNullOrWhiteSpace(largeImageKey)
                                ? null
                                : (object)new { large_image = largeImageKey, large_text = largeImageText }
                        }
                    }
                }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                WriteFrame(OpFrame, payload);
            }
            catch
            {
                lock (_sync)
                {
                    _connected = false;
                }
            }
        }

        public void ClearPresence()
        {
            if (!IsConnected)
            {
                return;
            }

            try
            {
                WriteFrame(OpFrame, JsonConvert.SerializeObject(new
                {
                    cmd = "SET_ACTIVITY",
                    nonce = Interlocked.Increment(ref _nonce).ToString(),
                    args = new { pid = System.Diagnostics.Process.GetCurrentProcess().Id }
                }));
            }
            catch
            {
                lock (_sync)
                {
                    _connected = false;
                }
            }
        }

        private void WriteFrame(int opcode, string json)
        {
            lock (_sync)
            {
                if (_pipe == null || !_pipe.IsConnected)
                {
                    _connected = false;
                    return;
                }

                var body = Encoding.UTF8.GetBytes(json);
                var frame = new byte[8 + body.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, frame, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 4, 4);
                Buffer.BlockCopy(body, 0, frame, 8, body.Length);

                _pipe.Write(frame, 0, frame.Length);
                _pipe.Flush();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;

                try
                {
                    if (_pipe != null && _pipe.IsConnected)
                    {
                        var body = Encoding.UTF8.GetBytes("{}");
                        var frame = new byte[8 + body.Length];
                        Buffer.BlockCopy(BitConverter.GetBytes(OpClose), 0, frame, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 4, 4);
                        Buffer.BlockCopy(body, 0, frame, 8, body.Length);
                        _pipe.Write(frame, 0, frame.Length);
                    }
                }
                catch
                {
                }

                try
                {
                    _pipe?.Dispose();
                }
                catch
                {
                }

                _pipe = null;
                _connected = false;
            }
        }
    }
}
