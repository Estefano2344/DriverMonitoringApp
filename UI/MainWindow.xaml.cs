using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DriverMonitoringApp.Networking;
using Newtonsoft.Json.Linq;

namespace DriverMonitoringApp.UI
{
    public partial class MainWindow : Window
    {
        private readonly WebSocketClient _webSocketClient;
        private bool _isProcessing;

        public MainWindow()
        {
            InitializeComponent();
            _webSocketClient = new WebSocketClient("ws://127.0.0.1:8000/video_stream");

            _webSocketClient.OnMessageReceived += ProcessServerMessage;
            _webSocketClient.OnDisconnected += HandleDisconnection;

            _ = _webSocketClient.ConnectAsync();
        }

        private void ProcessServerMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                JObject data = JObject.Parse(message);
                if (data["type"]?.ToString() == "alert")
                {
                    ShowAlert(data["message"].ToString());
                }
                else
                {
                    UpdateMonitoringLog($"[{DateTime.Now:HH:mm:ss}] {message}");
                }
            });
        }

        private void ShowAlert(string message)
        {
            AlertOverlay.Visibility = Visibility.Visible;
            AlertMessage.Text = message;
        }

        private async void CloseAlertButton_Click(object sender, RoutedEventArgs e)
        {
            AlertOverlay.Visibility = Visibility.Hidden;
            await _webSocketClient.SendMessageAsync("{\"type\":\"reset_confirm\"}");
        }

        private void HandleDisconnection()
        {
            Dispatcher.Invoke(() =>
            {
                UpdateMonitoringLog("Conexión perdida. Intentando reconectar...");
                _ = _webSocketClient.ConnectAsync();
            });
        }

        private void UpdateMonitoringLog(string message)
        {
            MonitoringLog.AppendText($"{message}\n");
            MonitoringLog.ScrollToEnd();
        }
    }
}
