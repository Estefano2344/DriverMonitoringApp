using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace DriverMonitoringApp
{
    public partial class MainWindow : Window
    {
        private bool isCameraOn = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            // Simular activación de la cámara
            isCameraOn = true;
            CameraPlaceholder.Background = Brushes.Green;

            // Esperar 800ms antes de mostrar la alerta
            await Task.Delay(800);

            if (isCameraOn)
            {
                ShowAlert();
            }
        }

        private void ShowAlert()
        {
            MessageBox.Show("¡DESPIERTA!", "Alerta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
