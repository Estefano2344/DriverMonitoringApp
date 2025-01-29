using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DriverMonitoringApp.Networking
{
    public class WebSocketClient
    {
        private ClientWebSocket _webSocket;
        private readonly Uri _serverUri;
        public event Action<string> OnMessageReceived;
        public event Action OnDisconnected;

        public WebSocketClient(string serverUrl)
        {
            _serverUri = new Uri(serverUrl);
            _webSocket = new ClientWebSocket();
        }

        public async Task ConnectAsync()
        {
            try
            {
                await _webSocket.ConnectAsync(_serverUri, CancellationToken.None);
                _ = Task.Run(ReceiveMessagesAsync);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error de conexión WebSocket: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 1024];

            while (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        OnMessageReceived?.Invoke(message);
                    }
                }
                catch
                {
                    OnDisconnected?.Invoke();
                    break;
                }
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrando conexión", CancellationToken.None);
            }
        }
    }
}
