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

namespace DriverMonitoringApp
{
    public partial class MainWindow : Window
    {
        private VideoCapture _capture = null;
        private bool isCameraOn = false;
        private bool isUsingServerVideo = false;
        private ClientWebSocket _webSocket;

        public MainWindow()
        {
            InitializeComponent();
            PopulateCameraSelector();
        }

        private void PopulateCameraSelector()
        {
            CameraSelector.Items.Clear();

            for (int i = 0; i < 10; i++)
            {
                using (var testCapture = new VideoCapture(i))
                {
                    if (testCapture.IsOpened)
                    {
                        CameraSelector.Items.Add(new ComboBoxItem
                        {
                            Content = $"Camera {i}",
                            Tag = i
                        });
                    }
                }
            }

            if (CameraSelector.Items.Count > 0)
            {
                CameraSelector.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("No se encontraron cámaras disponibles.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeCamera(int cameraIndex)
        {
            try
            {
                _capture = new VideoCapture(cameraIndex);
                if (!_capture.IsOpened)
                {
                    throw new Exception($"No se pudo abrir la cámara con índice {cameraIndex}.");
                }

                _capture.ImageGrabbed += ProcessFrameLocal;
                _capture.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar la cámara: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessFrameLocal(object sender, EventArgs e)
        {
            if (_capture != null && _capture.Ptr != IntPtr.Zero && !isUsingServerVideo)
            {
                using (var frame = new Mat())
                {
                    _capture.Retrieve(frame);
                    if (!frame.IsEmpty)
                    {
                        var imageBrush = new ImageBrush(ConvertToBitmapSource(frame));
                        CameraPlaceholder.Background = imageBrush;
                    }
                }
            }
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isCameraOn)
            {
                if (CameraSelector.SelectedItem is ComboBoxItem selectedCamera)
                {
                    int cameraIndex = (int)selectedCamera.Tag;
                    InitializeCamera(cameraIndex);
                    isCameraOn = true;
                    ActivateButton.Content = "APAGAR";
                }
                else
                {
                    MessageBox.Show("Selecciona una cámara de la lista.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                isCameraOn = false;
                if (_capture != null)
                {
                    _capture.Stop();
                    _capture.Dispose();
                    _capture = null;
                }
                ActivateButton.Content = "ENCENDER";
            }
        }

        private BitmapImage ConvertToBitmapSource(Mat mat)
        {
            using (var bitmap = mat.ToBitmap())
            {
                var bitmapImage = new BitmapImage();
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                    stream.Seek(0, SeekOrigin.Begin);
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = stream;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                }
                return bitmapImage;
            }
        }

        private void CloseAlertButton_Click(object sender, RoutedEventArgs e)
        {
            AlertOverlay.Visibility = Visibility.Hidden;
        }
    }
}
