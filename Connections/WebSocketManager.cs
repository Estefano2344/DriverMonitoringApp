using System;
using System.Collections.Generic;
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
                _webSocket?.Dispose();
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
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    var buffer = System.Text.Encoding.UTF8.GetBytes(message);
                    await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public async Task SendBinaryMessageAsync(byte[] data, CancellationToken cancellationToken)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
            finally
            {
                _webSocket.Dispose();
            }
        }

        public async Task StartVideoStreamAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Enviando comando start_stream...");  // <-- Log de diagnóstico
            await SendTextMessageAsync("start_stream", cancellationToken);
        }

        private async Task ReceiveMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[1024 * 1024];
            List<byte> accumulatedData = null;
            WebSocketMessageType? messageType = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
                        break;
                    }

                    if (accumulatedData == null)
                    {
                        messageType = result.MessageType;
                        accumulatedData = new List<byte>();
                    }

                    accumulatedData.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var messageData = accumulatedData.ToArray();
                        if (messageType == WebSocketMessageType.Text)
                        {
                            var message = System.Text.Encoding.UTF8.GetString(messageData);
                            OnTextMessageReceived?.Invoke(message);
                        }
                        else if (messageType == WebSocketMessageType.Binary)
                        {
                            OnBinaryMessageReceived?.Invoke(messageData);
                        }
                        accumulatedData = null;
                        messageType = null;
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