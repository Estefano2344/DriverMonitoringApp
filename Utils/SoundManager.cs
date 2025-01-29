using System;
using System.IO;
using System.Media;

namespace DriverMonitoringApp.Utils
{
    public class SoundManager
    {
        private SoundPlayer _soundPlayer;

        public void PlayAlertSound(string soundFile)
        {
            try
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, soundFile);
                if (File.Exists(fullPath))
                {
                    _soundPlayer = new SoundPlayer(fullPath);
                    _soundPlayer.Play();
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

        public void StopSound()
        {
            _soundPlayer?.Stop();
            _soundPlayer?.Dispose();
        }
    }
}
