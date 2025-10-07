using APR_Rednose.Commands;
using APR_Rednose.Model;
using APR_Rednose.Services;
using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace APR_Rednose.ViewModel
{
    public class PhotoInputViewModel : INotifyPropertyChanged
    {
        private ImageSource? _image;
        public ImageSource? Image
        {
            get => _image;
            private set { _image = value; OnPropertyChanged(); SaveCommand.RaiseCanExecuteChanged(); }
        }

        public RelayCommand OpenCommand { get; }
        public RelayCommand SaveCommand { get; }

        private BitmapSource? _lastResult;

        private readonly string _lmModelPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                         "Resources", "Assets", "shape_predictor_68_face_landmarks.dat");

        public PhotoInputViewModel()
        {
            OpenCommand = new RelayCommand(Open);
            SaveCommand = new RelayCommand(Save, () => _lastResult != null);
        }

        /// <summary>
        /// 불러온 결과/이미지 해제 (페이지 전환 시 메모리 정리)
        /// </summary>
        public void Clear()
        {
            _lastResult = null;
            Image = null;                    // private set 이므로 VM 내부에서만 가능
            SaveCommand?.RaiseCanExecuteChanged();
        }

        private void Open()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp|모든 파일|*.*"
                };
                if (dlg.ShowDialog() != true) return;

                var srcBmp = ImageProcessing.LoadBitmapSource(dlg.FileName);
                if (srcBmp == null)
                {
                    System.Windows.MessageBox.Show("이미지를 열 수 없습니다.");
                    return;
                }

                using var bgr = ImageProcessing.ToMat(srcBmp);   // 항상 BGR 3채널

                // 0) 랜드마크 모델 사용 가능 여부(파일 존재 + 접근성) 확인
                bool canUseLandmarks = false;
                try { canUseLandmarks = File.Exists(_lmModelPath); } catch { canUseLandmarks = false; }

                // 1) 얼굴 검출
                var rects = FaceDetection.DetectOpenCvRects(bgr);

                foreach (var r in rects)
                {
                    Point2f nose, left, right;
                    double faceW;

                    // 기본 추정 (항상 유효)
                    (nose, left, right, faceW) = EstimateFromRect(r);

                    // 2) 가능할 때만 랜드마크 시도 (실패해도 앱이 죽지 않도록 try/catch)
                    if (canUseLandmarks)
                    {
                        try
                        {
                            var res = FaceLandmarks.DetectNose(bgr, r, _lmModelPath);
                            if (res.HasValue)
                            {
                                var n = res.Value.NoseTip;
                                var l = res.Value.LeftNostril;
                                var rr = res.Value.RightNostril;

                                // 검증: 콧볼 간격이 비정상치면 추정값 유지
                                double d = Math.Sqrt((l.X - rr.X) * (l.X - rr.X) + (l.Y - rr.Y) * (l.Y - rr.Y));
                                double minD = r.Width * 0.10, maxD = r.Width * 0.32;

                                if (d >= minD && d <= maxD)
                                {
                                    nose = new Point2f(n.X, n.Y);
                                    left = new Point2f(l.X, l.Y);
                                    right = new Point2f(rr.X, rr.Y);
                                    faceW = r.Width;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("[PhotoInput] Landmark failed: " + ex.Message);
                            canUseLandmarks = false;
                        }
                    }

                    var center = new OpenCvSharp.Point((int)Math.Round(nose.X), (int)Math.Round(nose.Y));
                    var L = new OpenCvSharp.Point((int)Math.Round(left.X), (int)Math.Round(left.Y));
                    var R = new OpenCvSharp.Point((int)Math.Round(right.X), (int)Math.Round(right.Y));

                    try
                    {
                        RedNoseComposer.DrawSmart(bgr, center, L, R, faceW);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[PhotoInput] DrawSmart failed: " + ex.Message);
                    }
                }

                // 3) 출력
                var outBmp = ImageProcessing.ToBitmapSource(bgr);
                outBmp.Freeze();
                _lastResult = outBmp;
                Image = outBmp;
                SaveCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "이미지 처리 중 오류가 발생했습니다.\n\n" + ex.Message,
                    "Import Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private static (Point2f nose, Point2f left, Point2f right, double faceW)
        EstimateFromRect(OpenCvSharp.Rect r)
        {
            float cx = r.X + r.Width * 0.5f;
            float cy = r.Y + r.Height * 0.55f;     // 코끝 대략 55% 높이
            float half = (float)(r.Width * 0.12);  // 콧볼 간격 절반 ≈ 얼굴폭의 12%

            return (new Point2f(cx, cy),
                    new Point2f(cx - half, cy),
                    new Point2f(cx + half, cy),
                    r.Width);
        }

        private void Save()
        {
            if (_lastResult is not BitmapSource bmp) return;

            var dlg = new SaveFileDialog
            {
                Filter = "PNG 파일|*.png|JPG 파일|*.jpg;*.jpeg",
                FileName = "output.png"
            };
            if (dlg.ShowDialog() == true)
                ImageProcessing.SaveBitmapSource(bmp, dlg.FileName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
