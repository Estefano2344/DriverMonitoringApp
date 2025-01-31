using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DriverMonitoringApp
{
    /// <summary>
    /// Lógica de interacción para AlertWindow.xaml
    /// </summary>
    public partial class AlertWindow : Window
    {
        public event Action OnConfirmed; // Evento para notificar confirmación

        public AlertWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            OnConfirmed?.Invoke(); // Disparar evento
            this.Close();
        }
    }
}
