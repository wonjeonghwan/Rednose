using System;
using OpenCvSharp;

namespace APR_Rednose.Services
{
    public sealed class VideoRecorder : IDisposable
    {
        private VideoWriter? _writer;
        private Size _size;
        private double _fps;

        public bool IsOpen => _writer != null && _writer.IsOpened();

        public void Start(string path, Size frameSize, double fps = 30.0)
        {
            _size = frameSize;
            _fps = fps;

            // mp4v가 안되면 MJPG 등으로 교체 가능
            _writer = new VideoWriter(path, FourCC.MP4V, _fps, _size, isColor: true);
            if (!_writer.IsOpened())
                throw new InvalidOperationException("VideoWriter open failed. Try a different codec (e.g., MJPG).");
        }

        public void Write(Mat bgrFrame)
        {
            if (!IsOpen) return;

            // 안전: 3채널 BGR 보장
            if (bgrFrame.Channels() == 4)
                Cv2.CvtColor(bgrFrame, bgrFrame, ColorConversionCodes.BGRA2BGR);
            else if (bgrFrame.Channels() == 1)
                Cv2.CvtColor(bgrFrame, bgrFrame, ColorConversionCodes.GRAY2BGR);

            if (bgrFrame.Size() != _size)
            {
                using var tmp = new Mat();
                Cv2.Resize(bgrFrame, tmp, _size);
                _writer!.Write(tmp);
            }
            else
            {
                _writer!.Write(bgrFrame);
            }
        }

        public void Stop()
        {
            _writer?.Release();
            _writer?.Dispose();
            _writer = null;
        }

        public void Dispose() => Stop();
    }
}
