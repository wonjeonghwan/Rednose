using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using APR_Rednose.Commands;
using APR_Rednose.Services;
using OpenCvSharp;

namespace APR_Rednose.ViewModel
{
    public partial class CameraViewModel
    {
        // ==== 녹화 UI 바인딩 ====
        public RelayCommand StartRecordingCommand { get; private set; } = null!;
        public RelayCommand StopRecordingCommand { get; private set; } = null!;

        // Recording.cs에만 둠. CameraViewModel.cs에 동일 멤버가 있으면 삭제.
        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (_isRecording == value) return;
                _isRecording = value;
                OnPropertyChanged(); // nameof(IsRecording)
                // 커맨드 가용성 갱신
                StartRecordingCommand?.RaiseCanExecuteChanged();
                StopRecordingCommand?.RaiseCanExecuteChanged();
            }
        }

        // (선택) 카운트다운 표시가 필요할 때만 사용
        private bool _isCountingDown;
        public bool IsCountingDown
        {
            get => _isCountingDown;
            private set
            {
                if (_isCountingDown == value) return;
                _isCountingDown = value;
                OnPropertyChanged(); // nameof(IsCountingDown)
                StartRecordingCommand?.RaiseCanExecuteChanged();
            }
        }

        private string? _countdownText;
        public string? CountdownText
        {
            get => _countdownText;
            private set { _countdownText = value; OnPropertyChanged(); }
        }

        // ==== 내부 상태 ====
        private VideoRecorder? _recorder;
        private string? _tempRecordPath;

        // 메인(CameraViewModel.cs)의 OnFrame에서 갱신합니다.
        private Size _lastFrameSize = new Size(0, 0);

        private const double RECORD_FPS = 30.0;

        // 메인 생성자에서 호출됨
        private void InitRecordingCommands()
        {
            StartRecordingCommand = new RelayCommand(
                async () => await StartRecordingAsync(),
                () => _camera.IsRunning && !IsRecording && !IsCountingDown
            );

            StopRecordingCommand = new RelayCommand(
                async () => await StopRecordingAsync(),
                () => IsRecording
            );
        }

        private async Task StartRecordingAsync()
        {
            if (IsRecording || IsCountingDown) return;
            if (_lastFrameSize.Width <= 0 || _lastFrameSize.Height <= 0) return;

            // 3-2-1 카운트다운(원치 않으면 이 블록 삭제)
            IsCountingDown = true;
            for (int s = 3; s >= 1; s--) { CountdownText = s.ToString(); await Task.Delay(1000); }
            CountdownText = null;
            IsCountingDown = false;


            // 임시 파일 경로
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "APR_Rednose"));
            _tempRecordPath = Path.Combine(Path.GetTempPath(), "APR_Rednose",
                                           $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            _recorder = new VideoRecorder();
            _recorder.Start(_tempRecordPath, _lastFrameSize, RECORD_FPS);

            // 상태 플래그 ON
            IsRecording = true;
        }

        private async Task StopRecordingAsync()
        {
            if (!IsRecording) return;

            // 상태 플래그 OFF (UI 즉시 반영)
            IsRecording = false;

            _recorder?.Stop();
            _recorder?.Dispose();
            _recorder = null;

            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "동영상|*.mp4|AVI|*.avi",
                    FileName = $"rednose_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
                };
                if (dlg.ShowDialog() == true && _tempRecordPath != null && File.Exists(_tempRecordPath))
                {
                    File.Copy(_tempRecordPath, dlg.FileName, overwrite: true);
                }
            }
            finally
            {
                if (_tempRecordPath != null && File.Exists(_tempRecordPath))
                {
                    try { File.Delete(_tempRecordPath); } catch { }
                }
                _tempRecordPath = null;
                await Task.CompletedTask;
            }
        }

        // 메인 OnFrame에서 호출: 녹화 중이면 프레임 기록
        private void Recording_OnFrame(Mat bgrFrame)
        {
            if (IsRecording && _recorder?.IsOpen == true)
                _recorder.Write(bgrFrame);
        }

        // 메인 Dispose에서 호출
        private void Recording_Dispose()
        {
            try { _recorder?.Stop(); } catch { }
            _recorder?.Dispose();
            _recorder = null;
        }
    }
}
