using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DriverMonitoringApp.Connections;
using Emgu.CV;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DriverMonitoringApp.Views
{
    public partial class MainWindow : Window
    {
        private VideoCapture _capture;
        private WebSocketConnection _webSocketConnection;
        private CancellationTokenSource _processingCts;
        private readonly SemaphoreSlim _frameProcessingSemaphore = new SemaphoreSlim(1, 1);
        private bool _isProcessing;
        private bool _alertActive = false;
        private System.Media.SoundPlayer _currentSound;
        private System.Windows.Threading.DispatcherTimer _flashTimer;

        private const int FRAME_INTERVAL_MS = 33;
        private const string SERVER_URL = "ws://127.0.0.1:8000/video_stream";

        public MainWindow()
        {
            InitializeComponent();
            _webSocketConnection = new WebSocketConnection(SERVER_URL);
            _webSocketConnection.OnTextMessageReceived += HandleTextMessage;
            _webSocketConnection.OnBinaryMessageReceived += HandleBinaryMessage;
            _webSocketConnection.OnError += HandleWebSocketError;

            LoadAvailableCameras();
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

        private async Task StartProcessing()
        {
            try
            {
                await StopProcessing();

                _processingCts = new CancellationTokenSource();

                if (_capture == null || !_capture.IsOpened)
                {
                    InitializeCamera();
                }

                if (!_isProcessing)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CameraPlaceholder.Child = null; // Limpiar el placeholder
                    });

                    await _webSocketConnection.ConnectAsync(CancellationToken.None);

                    _isProcessing = true;
                    ToggleProcessingButton.Content = "Detener";
                    UpdateMonitoringLog("Iniciando monitoreo de somnolencia...");

                    // Iniciar el procesamiento de frames
                    _ = ProcessAndSendFrames(_processingCts.Token);
                }
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

                if (_processingCts != null)
                {
                    await _frameProcessingSemaphore.WaitAsync();
                    _processingCts.Cancel();
                    _processingCts.Dispose();
                    _processingCts = null;
                    _frameProcessingSemaphore.Release();
                }

                await _webSocketConnection.DisconnectAsync();

                _capture?.Dispose();
                _capture = null;

                await Dispatcher.InvokeAsync(() =>
                {
                    ToggleProcessingButton.Content = "Iniciar";
                    ToggleProcessingButton.IsEnabled = true;
                    UpdateMonitoringLog("Monitoreo detenido");
                    CameraPlaceholder.Background = new SolidColorBrush(Colors.Black);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al detener el procesamiento: {ex.Message}");
            }
        }

        private async Task ProcessAndSendFrames(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
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

                    if (_isProcessing)
                    {
                        byte[] frameData = frame.ToImage<Bgr, byte>()
                            .Convert<Bgr, byte>()
                            .ToJpegData(quality: 80);

                        await _webSocketConnection.SendBinaryMessageAsync(frameData, cancellationToken);
                    }

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

        private void CameraSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraSelector.SelectedItem != null)
            {
                try
                {
                    _capture?.Dispose();
                    int selectedIndex = CameraSelector.SelectedIndex;
                    _capture = new VideoCapture(selectedIndex);
                    _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 640);
                    _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 480);
                    UpdateMonitoringLog($"Cámara cambiada a dispositivo {selectedIndex}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cambiar la cámara: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseAlertButton_Click(object sender, RoutedEventArgs e)
        {
            AlertOverlay.Visibility = Visibility.Hidden;
            StopCurrentSound();
        }

        private void StopCurrentSound()
        {
            _currentSound?.Stop();
            _currentSound?.Dispose();
            _currentSound = null;
        }

        private void HandleTextMessage(string message)
        {
            Dispatcher.Invoke(() => ShowAlert(message));
        }

        private void HandleBinaryMessage(byte[] data)
        {
            Dispatcher.Invoke(async () =>
            {
                await ProcessReceivedFrame(data, data.Length);
            });
        }

        private void HandleWebSocketError(Exception ex)
        {
            Dispatcher.Invoke(() => MessageBox.Show($"Error en WebSocket: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error));
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

                CameraPlaceholder.Background = new ImageBrush(bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error procesando frame: {ex.Message}");
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

        // En MainWindow.xaml.cs
        private void LoadAvailableCameras()
        {
            // Implementación para cargar cámaras disponibles
            CameraSelector.Items.Clear();
            for (int i = 0; i < 10; i++)
            {
                using var cap = new VideoCapture(i);
                if (cap.IsOpened)
                {
                    CameraSelector.Items.Add($"Cámara {i}");
                }
            }
            CameraSelector.SelectedIndex = 0;
        }

        private void UpdateMonitoringLog(string message)
        {
            MonitoringLog.AppendText($"{message}\n");
            MonitoringLog.ScrollToEnd();
        }

        private void ShowAlert(string message)
        {
            Dispatcher.Invoke(() =>
            {
                AlertOverlay.Visibility = Visibility.Visible;
                AlertMessage.Text = message;
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Task.Run(async () => await StopProcessing()).Wait();
        }
    }
}
