using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace DriverMonitoringApp.Connections
{
    public class WebSocketConnection
    {
        private readonly string _serverUrl;
        private ClientWebSocket _webSocket;

        public event Action<string> OnTextMessageReceived;
        public event Action<byte[]> OnBinaryMessageReceived;
        public event Action<Exception> OnError;

        public WebSocketConnection(string serverUrl)
        {
            _serverUrl = serverUrl;
            _webSocket = new ClientWebSocket();
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_serverUrl), cancellationToken);
                _ = Task.Run(() => ReceiveMessages(cancellationToken));
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public async Task SendTextMessageAsync(string message, CancellationToken cancellationToken)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                var buffer = System.Text.Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cancellationToken);
            }
        }

        public async Task SendBinaryMessageAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, cancellationToken);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
            }
            _webSocket.Dispose();
        }

        private async Task ReceiveMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[1024 * 1024]; // 1MB buffer

            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                        OnTextMessageReceived?.Invoke(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        var data = new byte[result.Count];
                        Array.Copy(buffer, data, result.Count);
                        OnBinaryMessageReceived?.Invoke(data);
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }
    }
}
