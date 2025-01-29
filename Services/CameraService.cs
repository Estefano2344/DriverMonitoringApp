using System;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Drawing;

namespace DriverMonitoringApp.Services
{
    public class CameraService
    {
        private VideoCapture _capture;

        public bool InitializeCamera(int cameraIndex = 0)
        {
            try
            {
                _capture = new VideoCapture(cameraIndex);
                if (!_capture.IsOpened)
                {
                    throw new Exception("No se pudo abrir la cámara.");
                }

                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 640);
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 480);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al inicializar la cámara: {ex.Message}");
                return false;
            }
        }

        public Bitmap CaptureFrame()
        {
            using var frame = new Mat();
            _capture.Read(frame);
            return frame.IsEmpty ? null : frame.ToBitmap();
        }

        public void Dispose()
        {
            _capture?.Dispose();
        }
    }
}
