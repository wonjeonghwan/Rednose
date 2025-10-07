using System;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using APR_Rednose.Commands;
using APR_Rednose.Model; // ImageProcessing.SaveBitmapSource

namespace APR_Rednose.ViewModel
{
    public partial class CameraViewModel
    {
        public RelayCommand CaptureCommand { get; private set; } = null!;

        // 메인 생성자에서 호출됨
        private void InitCaptureCommand()
        {
            CaptureCommand = new RelayCommand(Capture, () => true);
        }

        private void Capture()
        {
            // 1) 미리보기 고정
            _freezePreview = true;

            var snap = _lastBitmap;   // 메인 파일에서 매 프레임 갱신됨
            if (snap != null)
            {
                // 고정된 스냅샷을 즉시 표시
                Preview = snap;

                // 2) 저장 다이얼로그
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG 파일|*.png|JPG 파일|*.jpg;*.jpeg",
                    FileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };
                if (dlg.ShowDialog() == true)
                {
                    ImageProcessing.SaveBitmapSource(snap, dlg.FileName);
                }
            }

            // 3) 라이브 복귀
            _freezePreview = false;
        }
    }
}
