using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace APR_Rednose.Services
{
    public interface ICameraService : IDisposable
    {
        event EventHandler<Mat>? FrameArrived;
        Task StartAsync(int cameraIndex = 0, CancellationToken ct = default);
        void Stop();
        bool IsRunning { get; }
    }
}
