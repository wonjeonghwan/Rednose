using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace APR_Rednose.Services
{
    public class OpenCVCameraService : ICameraService
    {
        private VideoCapture? _cap;
        private Task? _loopTask;
        private CancellationTokenSource? _cts;
        public event EventHandler<Mat>? FrameArrived;
        public bool IsRunning { get; private set; }

        public async Task StartAsync(int cameraIndex = 0, CancellationToken ct = default)
        {
            if (IsRunning) return;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cap = new VideoCapture(cameraIndex);
            if (!_cap.IsOpened())
                throw new InvalidOperationException("웹캠을 열 수 없습니다.");

            // 프레임 크기
            _cap.FrameWidth = 1280;
            _cap.FrameHeight = 720;

            IsRunning = true;
            _loopTask = Task.Run(() => CaptureLoop(_cts.Token));
            await Task.CompletedTask;
        }

        private void CaptureLoop(CancellationToken ct)
        {
            using var frame = new Mat();
            while (!ct.IsCancellationRequested)
            {
                if (!(_cap?.Read(frame) ?? false) || frame.Empty())
                    continue;

                // Mat 복사본 이벤트로 전달(받는 쪽에서 Dispose)
                FrameArrived?.Invoke(this, frame.Clone());
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            _loopTask?.Wait(200);
            _cap?.Release();
            _cap?.Dispose();
            _cts?.Dispose();
            _loopTask = null;
            _cts = null;
            _cap = null;
            IsRunning = false;
        }

        public void Dispose() => Stop();
    }
}
