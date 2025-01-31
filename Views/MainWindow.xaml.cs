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
                if (!_capture.IsOpened) throw new Exception("Cámara no detectada");

                // Configuración de resolución
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 640);
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 480);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cámara: {ex.Message}");
                _capture = null;
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
                    await _webSocketConnection.StartVideoStreamAsync(CancellationToken.None);

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
                    using var frame = new Mat();
                    if (_capture == null || !_capture.IsOpened) continue;

                    _capture.Read(frame);
                    if (frame.IsEmpty) continue;

                    // Convertir frame a JPEG
                    var jpegBytes = frame.ToImage<Bgr, byte>().ToJpegData(quality: 75);

                    // Enviar al servidor
                    await _webSocketConnection.SendBinaryMessageAsync(jpegBytes, cancellationToken);

                    await Task.Delay(FRAME_INTERVAL_MS, cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error frame: {ex.Message}");
                }
            }
        }

        private void CameraSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraSelector.SelectedItem != null)
            {
                try
                {
                    _capture?.Dispose(); // Liberar cámara anterior
                    int selectedIndex = CameraSelector.SelectedIndex;
                    _capture = new VideoCapture(selectedIndex);

                    if (!_capture.IsOpened)
                    {
                        MessageBox.Show($"No se pudo abrir la cámara {selectedIndex}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _capture = null;
                        return;
                    }

                    _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 640);
                    _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 480);
                    UpdateMonitoringLog($"Cámara cambiada a dispositivo {selectedIndex}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cambiar la cámara: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _capture = null;
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
            _currentSound = null; // Permitir reiniciar el sonido en la próxima alerta
        }

        private void HandleTextMessage(string message)
        {
            // Solo activar la alarma si el mensaje indica peligro
            if (message.Contains("PELIGRO") || message.Contains("somnolencia"))
            {
                Dispatcher.Invoke(() => ShowAlert(message));
            }
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

                // Reproducir el sonido de alerta
                if (_currentSound == null)
                {
                    string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Alerta_roja_Efecto_de_sonido.wav" // Nombre actualizado
);
                    if (File.Exists(soundPath))
                    {
                        _currentSound = new System.Media.SoundPlayer(soundPath);
                        _currentSound.PlayLooping(); // Repetir hasta que se detenga
                    }
                    else
                    {
                        MessageBox.Show("Archivo de sonido no encontrado", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _capture?.Dispose();
            Task.Run(async () => await StopProcessing()).Wait();
        }
    }
}
