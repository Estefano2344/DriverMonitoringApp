using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DriverMonitoringApp.Connections
{
    public class WebSocketConnection
    {
        private readonly string _serverUrl;
        private ClientWebSocket _webSocket;
        private bool _isReceiving;

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
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                    return; // Evita reconectar si ya está conectado

                DisposeWebSocket();
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_serverUrl), cancellationToken);

                _isReceiving = true; // Habilitar recepción
                _ = Task.Run(() => ReceiveMessages(cancellationToken));
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public async Task SendMessageAsync(byte[] data, WebSocketMessageType messageType, CancellationToken cancellationToken)
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(data), messageType, true, cancellationToken);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                }
            }
        }

        public Task SendTextMessageAsync(string message, CancellationToken cancellationToken) =>
            SendMessageAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, cancellationToken);

        public Task SendBinaryMessageAsync(byte[] data, CancellationToken cancellationToken) =>
            SendMessageAsync(data, WebSocketMessageType.Binary, cancellationToken);

        public async Task DisconnectAsync()
        {
            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
            finally
            {
                _isReceiving = false; // Detener recepción
                DisposeWebSocket();
            }
        }

        public async Task StartVideoStreamAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Enviando comando start_stream...");
            await SendTextMessageAsync("start_stream", cancellationToken);
        }

        private async Task ReceiveMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            using var memoryStream = new MemoryStream();

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isReceiving && _webSocket?.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await HandleCloseMessage();
                        break;
                    }

                    memoryStream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        ProcessMessage(memoryStream.ToArray(), result.MessageType);
                        memoryStream.SetLength(0); // Reiniciar el buffer sin reasignación
                    }
                }
            }
            catch (WebSocketException wsEx)
            {
                Console.WriteLine($"WebSocketException: {wsEx.Message}");
                OnError?.Invoke(wsEx);
                _isReceiving = false; // Evita intentos adicionales de recibir mensajes
            }
            catch (OperationCanceledException)
            {
                // La operación fue cancelada intencionalmente, no es un error
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        private async Task HandleCloseMessage()
        {
            _isReceiving = false; // Detener la recepción
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
            }
        }

        private void ProcessMessage(byte[] messageData, WebSocketMessageType messageType)
        {
            Task.Run(() =>
            {
                if (messageType == WebSocketMessageType.Text)
                {
                    OnTextMessageReceived?.Invoke(Encoding.UTF8.GetString(messageData));
                }
                else if (messageType == WebSocketMessageType.Binary)
                {
                    OnBinaryMessageReceived?.Invoke(messageData);
                }
            });
        }

        private void DisposeWebSocket()
        {
            _isReceiving = false;
            _webSocket?.Dispose();
            _webSocket = null;
        }
    }
}
