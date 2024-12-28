using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DriverMonitoringApp
{
    public partial class MainWindow : Window
    {
        private bool isCameraOn = false;
        private System.Media.SoundPlayer soundPlayer;
        private VideoCapture _capture = null;
        private DispatcherTimer _timer;
        private DispatcherTimer _logTimer;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAlertOverlay();
            InitializeLogSimulation(); // Inicializar simulación de logs
        }

        private void InitializeCamera()
        {
            try
            {
                _capture = new VideoCapture(0);
                _capture.ImageGrabbed += ProcessFrame;

                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
                };
                _timer.Tick += (s, e) =>
                {
                    if (_capture != null && _capture.Ptr != IntPtr.Zero)
                    {
                        _capture.Grab();
                    }
                };
                _timer.Start();

                CameraPlaceholder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar la cámara: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessFrame(object sender, EventArgs e)
        {
            if (_capture != null && _capture.Ptr != IntPtr.Zero)
            {
                using (var frame = new Mat())
                {
                    try
                    {
                        _capture.Retrieve(frame);
                        var imageBrush = new System.Windows.Media.ImageBrush(ConvertToBitmapSource(frame));
                        CameraPlaceholder.Background = imageBrush;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error al procesar el marco de video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private BitmapSource ConvertToBitmapSource(Mat mat)
        {
            using (System.Drawing.Bitmap bitmap = mat.ToBitmap())
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            StopCamera();
        }

        private void StopCamera()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.Dispose();
                    _capture = null;
                }

                if (_timer != null)
                {
                    _timer.Stop();
                    _timer = null;
                }

                CameraPlaceholder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al detener la cámara: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeAlertOverlay()
        {
            AlertOverlay.Visibility = Visibility.Hidden;
        }

        private void InitializeLogSimulation()
        {
            // Simular líneas de código en el panel de monitoreo
            _logTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Cada 500ms se imprime una nueva línea
            };
            _logTimer.Tick += (s, e) =>
            {
                MonitoringLog.Text += $"[INFO] Simulando proceso: {DateTime.Now:HH:mm:ss.fff}\n";
                MonitoringLog.ScrollToEnd(); // Mantener el scroll en la última línea
            };
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isCameraOn)
            {
                isCameraOn = true;
                InitializeCamera();
                _logTimer.Start();
                ActivateButton.Content = "APAGAR";
            }
            else
            {
                isCameraOn = false;
                StopCamera();
                _logTimer.Stop();
                ActivateButton.Content = "ENCENDER";
            }
        }

        private void ShowAlert()
        {
            AlertOverlay.Visibility = Visibility.Visible;

            string soundFilePath = "Alerta roja ｜ Efecto de sonido.wav";

            if (!System.IO.File.Exists(soundFilePath))
            {
                MessageBox.Show($"El archivo de sonido no se encuentra: {System.IO.Path.GetFullPath(soundFilePath)}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                soundPlayer = new System.Media.SoundPlayer(soundFilePath);
                soundPlayer.PlayLooping();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al reproducir el sonido: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseAlertButton_Click(object sender, RoutedEventArgs e)
        {
            AlertOverlay.Visibility = Visibility.Hidden;

            if (soundPlayer != null)
            {
                soundPlayer.Stop();
            }
        }
    }
}
