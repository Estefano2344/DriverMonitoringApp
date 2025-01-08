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
using Newtonsoft.Json; // Agregar este using
using Newtonsoft.Json.Linq; // Agregar este using

namespace DriverMonitoringApp
{
    public partial class MainWindow : Window
    {
        private VideoCapture _capture;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _processingCts;
        private readonly SemaphoreSlim _frameProcessingSemaphore = new SemaphoreSlim(1, 1);
        private bool _isProcessing;
        private bool _alertActive = false;
        private System.Media.SoundPlayer _currentSound;

        // Constantes
        private const int FRAME_INTERVAL_MS = 33; // ~30 FPS
        private const int RECONNECT_DELAY_MS = 5000;
        private const string SERVER_URL = "ws://127.0.0.1:8000/video_stream";

        public MainWindow()
        {
            InitializeComponent();
            LoadAvailableCameras(); // Agregar esta línea
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

        private BitmapSource ConvertToBitmapSource(Mat frame)
        {
            try
            {
                using var image = frame.ToImage<Bgr, byte>();
                var bitmap = image.ToBitmap();
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Dispose();
                return bitmapSource;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error convirtiendo frame: {ex.Message}");
                return null;
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

                    if (_webSocket?.State == WebSocketState.Open)
                    {
                        byte[] frameData = frame.ToImage<Bgr, byte>()
                            .Convert<Bgr, byte>()
                            .ToJpegData(quality: 80);

                        await _webSocket.SendAsync(
                            new ArraySegment<byte>(frameData),
                            WebSocketMessageType.Binary,
                            true,
                            cancellationToken);
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var bitmapSource = ConvertToBitmapSource(frame);
                            if (bitmapSource != null)
                            {
                                if (bitmapSource.CanFreeze) bitmapSource.Freeze();
                                CameraPlaceholder.Background = new ImageBrush(bitmapSource);
                            }
                        });
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
                // Asegurarse de que los recursos anteriores estén liberados
                await StopProcessing();

                // Inicializar nueva sesión
                _processingCts = new CancellationTokenSource();
                
                if (_capture == null || !_capture.IsOpened)
                {
                    InitializeCamera();
                }

                if (!_isProcessing)
                {
                    // Limpiar el placeholder de la cámara
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CameraPlaceholder.Child = null; // Eliminar el TextBlock placeholder
                    });

                    await InitializeWebSocket();
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

                // Detener el procesamiento de frames
                if (_processingCts != null)
                {
                    await _frameProcessingSemaphore.WaitAsync();
                    _processingCts.Cancel();
                    _processingCts.Dispose();
                    _processingCts = null;
                    _frameProcessingSemaphore.Release();
                }

                // Cerrar websocket
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Cerrando conexión",
                        CancellationToken.None);
                    _webSocket.Dispose();
                    _webSocket = null;
                }

                // Reiniciar la cámara
                _capture?.Dispose();
                _capture = null;
                InitializeCamera();

                // Actualizar UI
                await Dispatcher.InvokeAsync(() =>
                {
                    ToggleProcessingButton.Content = "Iniciar";
                    ToggleProcessingButton.IsEnabled = true;
                    UpdateMonitoringLog("Monitoreo detenido");
                    // Limpiar la imagen de la cámara
                    CameraPlaceholder.Background = new SolidColorBrush(Colors.Black);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al detener el procesamiento: {ex.Message}");
            }
        }

        private void ShowAlert(string message)
        {
            try
            {
                // Primero intentar parsear como mensaje de sistema
                try
                {
                    JObject systemMessage = JObject.Parse(message);
                    if (systemMessage["type"]?.Value<string>() == "reset_confirm")
                    {
                        // Restaurar UI a estado normal
                        Dispatcher.Invoke(() =>
                        {
                            this.Background = new SolidColorBrush(Colors.White);
                            AlertOverlay.Visibility = Visibility.Collapsed;
                            _alertActive = false;
                        });
                        return;
                    }
                }
                catch { /* No es un mensaje de sistema, continuar con el procesamiento normal */ }

                // Procesar alerta normal
                JObject alertData = JObject.Parse(message);
                int alertLevel = alertData["level"].Value<int>();
                string alertMessage = alertData["message"].Value<string>();
                double elapsedTime = alertData["elapsed_time"].Value<double>();

                // Solo procesar si no hay alerta activa o si es una actualización de la alerta actual
                if (!_alertActive || alertLevel > 1)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (alertLevel == 2)
                        {
                            _alertActive = true;
                            AlertOverlay.Visibility = Visibility.Visible;
                            AlertMessage.Text = alertMessage;
                            AlertBorder.Background = new SolidColorBrush(Colors.Red);
                            PlayAlertSound("Alerta roja ｜ Efecto de sonido.wav");
                            FlashWindow();
                        }
                        else
                        {
                            UpdateMonitoringLog($"[{DateTime.Now:HH:mm:ss}] {alertMessage} ({elapsedTime:F1}s)");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error procesando alerta: {ex.Message}");
            }
        }

        private async void CloseAlertButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Detener el sonido inmediatamente
                StopCurrentSound();

                // Deshabilitar el botón temporalmente para evitar doble click
                if (sender is Button button)
                    button.IsEnabled = false;

                // Ocultar la alerta inmediatamente
                AlertOverlay.Visibility = Visibility.Collapsed;
                this.Background = new SolidColorBrush(Colors.White);
                
                if (_webSocket?.State == WebSocketState.Open)
                {
                    // Enviar señal de reinicio al servidor
                    var resetMessage = new byte[] { 0 };
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(resetMessage),
                        WebSocketMessageType.Binary,
                        true,
                        CancellationToken.None);
                    
                    UpdateMonitoringLog("Alerta crítica confirmada por el usuario");
                    UpdateMonitoringLog("Esperando confirmación del servidor...");
                }
                else
                {
                    // Si no hay conexión, restaurar estado localmente
                    _alertActive = false;
                    UpdateMonitoringLog("Alerta cancelada (modo sin conexión)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cerrar alerta: {ex.Message}");
            }
            finally
            {
                // Re-habilitar el botón
                if (sender is Button button)
                    button.IsEnabled = true;
            }
        }

        private void PlayAlertSound(string soundFile)
        {
            try
            {
                StopCurrentSound();
                
                string fullPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, 
                    soundFile);

                if (File.Exists(fullPath))
                {
                    _currentSound = new System.Media.SoundPlayer(fullPath);
                    _currentSound.Play();
                }
                else
                {
                    Console.WriteLine($"Archivo de sonido no encontrado: {fullPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reproduciendo sonido: {ex.Message}");
            }
        }

        private void StopCurrentSound()
        {
            try
            {
                if (_currentSound != null)
                {
                    _currentSound.Stop();
                    _currentSound.Dispose();
                    _currentSound = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deteniendo sonido: {ex.Message}");
            }
        }

        private void FlashWindow()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            
            var flashCount = 0;
            timer.Tick += (s, e) =>
            {
                if (flashCount++ >= 6)
                {
                    timer.Stop();
                    return;
                }
                
                this.Background = flashCount % 2 == 0 
                    ? new SolidColorBrush(Colors.Red) 
                    : new SolidColorBrush(Colors.White);
            };
            
            timer.Start();
        }

        private void UpdateMonitoringLog(string message)
        {
            MonitoringLog.AppendText($"{message}\n");
            MonitoringLog.ScrollToEnd();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                StopCurrentSound();  // Asegurar que el sonido se detiene al cerrar
                // Esperar a que se complete el cierre de recursos
                Task.Run(async () => await StopProcessing()).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cerrar la aplicación: {ex.Message}");
            }
            finally
            {
                _frameProcessingSemaphore?.Dispose();
                _processingCts?.Dispose();
                _capture?.Dispose();
                _webSocket?.Dispose();
            }
        }

        private void CameraSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraSelector.SelectedItem != null)
            {
                try
                {
                    // Detener la cámara actual si está activa
                    _capture?.Dispose();
                    
                    // Iniciar nueva cámara con el índice seleccionado
                    int selectedIndex = CameraSelector.SelectedIndex;
                    _capture = new VideoCapture(selectedIndex);
                    
                    if (!_capture.IsOpened)
                    {
                        throw new Exception("No se pudo abrir la cámara seleccionada.");
                    }

                    // Configurar resolución
                    _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 640);
                    _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 480);

                    UpdateMonitoringLog($"Cámara cambiada al dispositivo {selectedIndex}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al cambiar la cámara: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadAvailableCameras()
        {
            CameraSelector.Items.Clear();
            
            // Buscar cámaras disponibles (típicamente 0-9)
            for (int i = 0; i < 10; i++)
            {
                using (var cap = new VideoCapture(i))
                {
                    if (cap.IsOpened)
                    {
                        CameraSelector.Items.Add($"Cámara {i}");
                    }
                }
            }

            // Seleccionar la primera cámara por defecto si hay alguna
            if (CameraSelector.Items.Count > 0)
            {
                CameraSelector.SelectedIndex = 0;
            }
        }
    }
}