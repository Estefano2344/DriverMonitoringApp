using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;

namespace DriverMonitoringApp
{
    public partial class MainWindow : Window
    {
        private VideoCapture _capture;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _processingCts;
        private readonly SemaphoreSlim _frameProcessingSemaphore = new SemaphoreSlim(1, 1);
        private bool _isProcessing;

        // Constantes
        private const int FRAME_INTERVAL_MS = 33; // ~30 FPS
        private const int RECONNECT_DELAY_MS = 5000;
        private const string SERVER_URL = "ws://127.0.0.1:8000/video_stream";

        public MainWindow()
        {
            InitializeComponent();
            InitializeCamera();
        }

        private void InitializeCamera()
        {
            try
            {
                _capture = new VideoCapture(0);
                if (!_capture.IsOpened)
                {
                    throw new Exception("No se pudo abrir la cámara.");
                }

                // Configurar resolución de la cámara
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 640);
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 480);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar la cámara: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task InitializeWebSocket()
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(SERVER_URL), CancellationToken.None);

                _ = Task.Run(ReceiveServerMessages);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al conectar con el servidor: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReceiveServerMessages()
        {
            var buffer = new byte[1024 * 1024]; // 1MB buffer

            while (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        await ProcessReceivedFrame(buffer, result.Count);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = System.Text.Encoding.UTF8.GetString(
                            buffer, 0, result.Count);
                        await Dispatcher.InvokeAsync(() => ShowAlert(message));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error recibiendo mensajes: {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }

        private async Task ProcessReceivedFrame(byte[] frameData, int count)
        {
            try
            {
                using var stream = new MemoryStream(frameData, 0, count);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    CameraPlaceholder.Background = new ImageBrush(bitmap);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error procesando frame: {ex.Message}");
            }
        }

        private async Task ProcessAndSendFrames(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _frameProcessingSemaphore.WaitAsync();

                    using var frame = new Mat();
                    _capture.Read(frame);

                    if (frame.IsEmpty)
                    {
                        continue;
                    }

                    // Comprimir frame como JPEG con calidad moderada
                    byte[] frameData = frame.ToImage<Bgr, byte>()
                        .Convert<Bgr, byte>()
                        .ToJpegData(quality: 80);

                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(frameData),
                        WebSocketMessageType.Binary,
                        true,
                        cancellationToken);

                    await Task.Delay(FRAME_INTERVAL_MS, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error procesando frame: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                }
                finally
                {
                    _frameProcessingSemaphore.Release();
                }
            }
        }

        private async void ToggleProcessingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isProcessing)
            {
                await StartProcessing();
            }
            else
            {
                await StopProcessing();
            }
        }

        private async Task StartProcessing()
        {
            try
            {
                await InitializeWebSocket();
                _processingCts = new CancellationTokenSource();
                _isProcessing = true;
                ToggleProcessingButton.Content = "Detener";

                _ = ProcessAndSendFrames(_processingCts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al iniciar el procesamiento: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await StopProcessing();
            }
        }

        private async Task StopProcessing()
        {
            try
            {
                _isProcessing = false;
                _processingCts?.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Cerrando conexión",
                        CancellationToken.None);
                }

                _webSocket?.Dispose();
                _webSocket = null;
                ToggleProcessingButton.Content = "Iniciar";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al detener el procesamiento: {ex.Message}");
            }
        }

        private void ShowAlert(string message)
        {
            AlertOverlay.Visibility = Visibility.Visible;
            AlertMessage.Text = message;

            try
            {
                var soundPlayer = new System.Media.SoundPlayer("alert.wav");
                soundPlayer.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reproduciendo sonido de alerta: {ex.Message}");
            }

            // Programar el cierre automático de la alerta después de 5 segundos
            Task.Delay(5000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    AlertOverlay.Visibility = Visibility.Collapsed;
                });
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _ = StopProcessing();
            _capture?.Dispose();
            _frameProcessingSemaphore?.Dispose();
            _processingCts?.Dispose();
        }
    }
}