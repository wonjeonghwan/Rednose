using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using APR_Rednose.Commands;
using APR_Rednose.Model;
using APR_Rednose.Services;
using OpenCvSharp;

namespace APR_Rednose.ViewModel
{
    public partial class CameraViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ICameraService _camera;
        private readonly Dispatcher _ui;

        // 반지름 스무딩(검출→트랙 병합)
        private const double RADIUS_BLEND_DETECT = 0.12;
        // 얼굴폭 힌트 EMA
        private const double FACEW_BLEND = 0.2;          // 얼굴폭 부드럽게
        // 반지름 변화 최대 폭( px)
        private const double RADIUS_MAX_STEP = 2.0;

        // deadband 임계
        private const double RADIUS_DEADBAND = 1.1; // 1~1.5px 권장

        private ImageSource? _preview;
        public ImageSource? Preview
        {
            get => _preview;
            private set { _preview = value; OnPropertyChanged(); }
        }

        private string? _cameraError;
        public string? CameraError
        {
            get => _cameraError;
            private set { _cameraError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCameraError)); }
        }
        public bool HasCameraError => !string.IsNullOrEmpty(CameraError);

        // ====== 미러(좌우 반전) ======
        private bool _isMirror = true;
        public bool IsMirror
        {
            get => _isMirror;
            set { _isMirror = value; OnPropertyChanged(); }
        }

        // 버튼으로 토글하고 싶을 때 사용
        public RelayCommand ToggleMirrorCommand { get; private set; } = null!;

        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }

        // ====== 추적/검출 파라미터 ======
        private volatile bool _detectBusy = false;
        private DateTime _lastDetect = DateTime.MinValue;

        // 검출 간격
        private readonly TimeSpan _detectInterval = TimeSpan.FromMilliseconds(70);

        private readonly object _lock = new();

        private Mat? _prevGray; // LK Optical Flow용 이전 그레이 프레임

        // 각 얼굴의 코 추적 정보(코끝/콧볼 좌표)
        private class NoseTrack
        {
            public Point2f Nose;
            public Point2f Left;
            public Point2f Right;
            public double FaceWidthHint;  // 최근 얼굴 폭(px)
            public double LastRadius;     // 최근 사용 반지름(px)
            public DateTime LastRefresh;
        }

        private List<NoseTrack> _tracks = new();

        private readonly string _lmModelPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                   "Resources", "Assets", "shape_predictor_68_face_landmarks.dat");

        // EMA(지수평활) 계수: 탐지→트랙 병합 시 부드럽게
        private const double DETECT_BLEND = 0.4;   // 0~1 (높을수록 새 좌표에 가중)
        private const double FLOW_BLEND = 0.80;  // 프레임마다 흐름 결과와 블렌딩
        private const double MATCH_THRESH = 60.0;  // 탐지-트랙 매칭 허용 거리(px)
        private static double Dist(Point2f a, Point2f b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        // ====== 캡처(미리보기 고정) 상태 ======
        private bool _freezePreview = false;
        private BitmapSource? _lastBitmap;  // 마지막으로 표시한 프레임(저장용 스냅샷)

                // 얼굴 사각형으로부터 코 위치/콧볼을 예측 (가림/실패시 사용)
        private static (Point2f nose, Point2f left, Point2f right, double faceW)
        EstimateFromRect(OpenCvSharp.Rect r)
        {
            float cx = r.X + r.Width * 0.5f;
            float cy = r.Y + r.Height * 0.55f;       // 코끝은 대략 얼굴 높이의 55%
            float half = (float)(r.Width * 0.12);    // 콧볼 간격의 절반 ~ 얼굴폭 12%

            return (new Point2f(cx, cy),
                    new Point2f(cx - half, cy),
                    new Point2f(cx + half, cy),
                    r.Width);
        }

        public CameraViewModel(ICameraService camera)
        {
            _camera = camera;
            _ui = Dispatcher.CurrentDispatcher;

            _camera.FrameArrived += OnFrame;

            StartCommand = new RelayCommand(async () => await StartAsync(), () => !_camera.IsRunning);
            StopCommand = new RelayCommand(() => _camera.Stop(), () => _camera.IsRunning);

            ToggleMirrorCommand = new RelayCommand(() => IsMirror = !IsMirror);

            // ✅ 녹화/캡처 커맨드 초기화 (partial 메서드)
            InitRecordingCommands();
            InitCaptureCommand();
        }

        private async Task StartAsync()
        {
            // 이전 에러 초기화
            CameraError = null;

            try
            {
                await _camera.StartAsync();            // <- OpenCV VideoCapture 시도
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
            catch
            {
                // 카메라 미연결/열기 실패 시
                CameraError = "카메라를 연결할 수 없습니다. 연결 상태를 확인해주세요";

                // 혹시 열리다 만 리소스 정리
                try { _camera.Stop(); } catch { }

                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();

                // 여기서 return하면 이후 OnFrame도 안 들어오니 안전
                return;
            }
        }


        private void OnFrame(object? s, Mat mat)
        {
            // 기본/토글에 따라 좌우 반전 (프리뷰+녹화 모두 적용)
            if (IsMirror)
                Cv2.Flip(mat, mat, FlipMode.Y);  // Y = horizontal flip

            // 그레이 준비 (LK 플로우용)
            using var gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

            // --- 1) 프레임마다 LK Optical Flow 로 추적 업데이트 ---
            List<NoseTrack> drawTracks;
            lock (_lock)
            {
                if (_prevGray != null && _tracks.Count > 0)
                {
                    // 모든 트랙의 3점을 하나의 배열로 묶기
                    var prevPts = _tracks.SelectMany(t => new[] { t.Nose, t.Left, t.Right }).ToArray();

                    // ✅ 배열 → Mat 래핑 (배열 생성자 X, Set로 채움)
                    using var prevPtsMat = new Mat(prevPts.Length, 1, MatType.CV_32FC2);
                    for (int i = 0; i < prevPts.Length; i++)
                        prevPtsMat.Set(i, 0, prevPts[i]);

                    using var nextPtsMat = new Mat(prevPts.Length, 1, MatType.CV_32FC2);
                    using var statusMat = new Mat(prevPts.Length, 1, MatType.CV_8UC1);
                    using var errMat = new Mat(prevPts.Length, 1, MatType.CV_32FC1);

                    // ✅ LK 계산 (Input/Output Mat 버전)
                    Cv2.CalcOpticalFlowPyrLK(
                        _prevGray!,              // prevImg
                        gray,                    // nextImg
                        prevPtsMat,              // prevPts
                        nextPtsMat,              // nextPts (out)
                        statusMat,               // status  (out)
                        errMat,                  // err     (out)
                        new Size(21, 21),
                        3,
                        new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.Count, 30, 0.01),
                        OpticalFlowFlags.None,
                        1.0e-4
                    );

                    // Mat → 배열로 꺼내기
                    var nextPts = new Point2f[prevPts.Length];
                    var status = new byte[prevPts.Length];
                    for (int i = 0; i < prevPts.Length; i++)
                    {
                        nextPts[i] = nextPtsMat.Get<Point2f>(i, 0);
                        status[i] = statusMat.Get<byte>(i, 0);
                    }

                    for (int ti = 0; ti < _tracks.Count; ti++)
                    {
                        int i0 = ti * 3;
                        // 3점 중 2점 이상 유효하면 업데이트
                        int valid = (status[i0] == 1 ? 1 : 0) + (status[i0 + 1] == 1 ? 1 : 0) + (status[i0 + 2] == 1 ? 1 : 0);
                        if (valid >= 2)
                        {
                            var nN = nextPts[i0];
                            var nL = nextPts[i0 + 1];
                            var nR = nextPts[i0 + 2];

                            // EMA로 부드럽게 이동
                            _tracks[ti].Nose = new Point2f(
                                (float)((1 - FLOW_BLEND) * _tracks[ti].Nose.X + FLOW_BLEND * nN.X),
                                (float)((1 - FLOW_BLEND) * _tracks[ti].Nose.Y + FLOW_BLEND * nN.Y));
                            _tracks[ti].Left = new Point2f(
                                (float)((1 - FLOW_BLEND) * _tracks[ti].Left.X + FLOW_BLEND * nL.X),
                                (float)((1 - FLOW_BLEND) * _tracks[ti].Left.Y + FLOW_BLEND * nL.Y));
                            _tracks[ti].Right = new Point2f(
                                (float)((1 - FLOW_BLEND) * _tracks[ti].Right.X + FLOW_BLEND * nR.X),
                                (float)((1 - FLOW_BLEND) * _tracks[ti].Right.Y + FLOW_BLEND * nR.Y));
                        }
                    }
                }

                // 드로잉용 스냅샷
                drawTracks = _tracks.Select(t => new NoseTrack
                {
                    Nose = t.Nose,
                    Left = t.Left,
                    Right = t.Right,
                    FaceWidthHint = t.FaceWidthHint,   // ← 추가
                    LastRadius = t.LastRadius,      // ← 추가 (가장 중요)
                    LastRefresh = t.LastRefresh
                }).ToList();

            }

            // --- 2) 그리기 ---
            using var toShow = mat.Clone();
            foreach (var t in drawTracks)
            {
                var center = new OpenCvSharp.Point(
                    (int)Math.Round(t.Nose.X), (int)Math.Round(t.Nose.Y));

                // 반지름은 '검출 갱신 시'에만 업데이트됨
                // 재계산하지 않고 t.LastRadius만 사용
                double r = (t.LastRadius > 0) ? t.LastRadius : 10.0; // 초기값

                RedNoseComposer.DrawWithRadius(toShow, center, r);
            }


            // 녹화(녹화 partial에 정의)
            _lastFrameSize = toShow.Size();
            Recording_OnFrame(toShow);

            // 미리보기 업데이트 (캡처 중이면 고정)
            var bmp = ImageProcessing.ToBitmapSource(toShow);
            bmp.Freeze();

            if (!_freezePreview)
            {
                _lastBitmap = bmp; // 캡처용 최신 스냅샷 갱신
                _ui.BeginInvoke(DispatcherPriority.Render, new Action(() => Preview = bmp));
            }

            // 3) 주기적 재검출(0.10s) → 트랙 보정/보완 ---
            if (!_detectBusy && (DateTime.UtcNow - _lastDetect) > _detectInterval)
            {
                _detectBusy = true;
                _lastDetect = DateTime.UtcNow;
                var img = mat.Clone(); // 백그라운드용

                Task.Run(() =>
                {
                    try
                    {
                        var rects = FaceDetection.DetectOpenCvRects(img);

                        var detected = new List<(Point2f nose, Point2f left, Point2f right, double faceW)>(rects.Length);

                        foreach (var r in rects)
                        {
                            Point2f nose = default, left = default, right = default; // 초기화
                            double faceW = 0;

                            try
                            {
                                var res = FaceLandmarks.DetectNose(img, r, _lmModelPath);
                                if (res.HasValue)
                                {
                                    var n = res.Value.NoseTip;
                                    var l = res.Value.LeftNostril;
                                    var rr = res.Value.RightNostril;

                                    nose = new Point2f(n.X, n.Y);
                                    left = new Point2f(l.X, l.Y);
                                    right = new Point2f(rr.X, rr.Y);
                                    faceW = r.Width;

                                    // 콧볼 간격이 얼굴폭 대비 비정상(너무 작거나 너무 큰)하면 fallback
                                    double d = Math.Sqrt((left.X - right.X) * (left.X - right.X) + (left.Y - right.Y) * (left.Y - right.Y));
                                    double minD = r.Width * 0.10;   // 10% ~
                                    double maxD = r.Width * 0.32;   // ~ 32%
                                    if (d < minD || d > maxD)
                                        (nose, left, right, faceW) = EstimateFromRect(r);
                                }
                                else
                                {
                                    (nose, left, right, faceW) = EstimateFromRect(r);
                                }
                            }
                            catch
                            {
                                (nose, left, right, faceW) = EstimateFromRect(r);
                            }

                            detected.Add((nose, left, right, faceW));
                        }

                        lock (_lock)
                        {
                            // 기존 트랙과 매칭해서 부드럽게 업데이트
                            foreach (var d in detected)
                            {
                                // 가장 가까운 기존 트랙 찾기
                                int bestIdx = -1;
                                double bestDist = double.MaxValue;
                                for (int i = 0; i < _tracks.Count; i++)
                                {
                                    double d0 = Dist(_tracks[i].Nose, d.nose);
                                    if (d0 < bestDist) { bestDist = d0; bestIdx = i; }
                                }

                                if (bestIdx != -1 && bestDist < MATCH_THRESH)
                                {
                                    // 블렌딩(탐지값에 가중치)
                                    _tracks[bestIdx].Nose = new Point2f(
                                        (float)((1 - DETECT_BLEND) * _tracks[bestIdx].Nose.X + DETECT_BLEND * d.nose.X),
                                        (float)((1 - DETECT_BLEND) * _tracks[bestIdx].Nose.Y + DETECT_BLEND * d.nose.Y));
                                    _tracks[bestIdx].Left = new Point2f(
                                        (float)((1 - DETECT_BLEND) * _tracks[bestIdx].Left.X + DETECT_BLEND * d.left.X),
                                        (float)((1 - DETECT_BLEND) * _tracks[bestIdx].Left.Y + DETECT_BLEND * d.left.Y));
                                    _tracks[bestIdx].Right = new Point2f(
                                        (float)((1 - DETECT_BLEND) * _tracks[bestIdx].Right.X + DETECT_BLEND * d.right.X),
                                        (float)((1 - DETECT_BLEND) * _tracks[bestIdx].Right.Y + DETECT_BLEND * d.right.Y));
                                    _tracks[bestIdx].LastRefresh = DateTime.UtcNow;
                                    // 기존 foreach (var d in detected) { ... } 안의 매칭 분기 내에서
                                    // 1) 얼굴폭 힌트를 EMA로 안정화
                                    _tracks[bestIdx].FaceWidthHint =
                                        (_tracks[bestIdx].FaceWidthHint > 0)
                                          ? (1 - FACEW_BLEND) * _tracks[bestIdx].FaceWidthHint + FACEW_BLEND * d.faceW
                                          : d.faceW;

                                    // 2) 안정화된 faceW로 반지름 후보 계산(ComputeRadius 사용)
                                    double fwh = _tracks[bestIdx].FaceWidthHint;
                                    double candR = RedNoseComposer.ComputeRadius(
                                        new OpenCvSharp.Point((int)Math.Round(d.nose.X), (int)Math.Round(d.nose.Y)),
                                        new OpenCvSharp.Point((int)Math.Round(d.left.X), (int)Math.Round(d.left.Y)),
                                        new OpenCvSharp.Point((int)Math.Round(d.right.X), (int)Math.Round(d.right.Y)),
                                        fwh);

                                    // (선택) 변화량 최대폭 제한을 먼저 하고 있으면 그‘다음’에 둬도 되고, 없으면 바로 둬도 됨.
                                    double prevR = _tracks[bestIdx].LastRadius;

                                    // deadband: 미세 변화는 무시
                                    double delta = candR - prevR;
                                    if (Math.Abs(delta) < RADIUS_DEADBAND)
                                    {
                                        candR = prevR; // 변화 무시
                                    }

                                    else if (prevR > 0)
                                    {
                                        // 변화량 최대 폭 제한
                                        double up = prevR + RADIUS_MAX_STEP;
                                        double down = prevR - RADIUS_MAX_STEP;
                                        if (candR > up) candR = up;
                                        else if (candR < down) candR = down;
                                    }

                                    // 4) 마지막으로 반지름 EMA(크기만 부드럽게)
                                    _tracks[bestIdx].LastRadius =
                                        (1 - RADIUS_BLEND_DETECT) * prevR + RADIUS_BLEND_DETECT * candR;
                                }
                                else
                                {
                                    // 새 트랙 생성
                                    var fwh0 = d.faceW; // 최초 얼굴폭
                                    var initR = RedNoseComposer.ComputeRadius(
                                        new OpenCvSharp.Point((int)Math.Round(d.nose.X), (int)Math.Round(d.nose.Y)),
                                        new OpenCvSharp.Point((int)Math.Round(d.left.X), (int)Math.Round(d.left.Y)),
                                        new OpenCvSharp.Point((int)Math.Round(d.right.X), (int)Math.Round(d.right.Y)),
                                        fwh0);

                                    _tracks.Add(new NoseTrack
                                    {
                                        Nose = d.nose,
                                        Left = d.left,
                                        Right = d.right,
                                        FaceWidthHint = fwh0,
                                        LastRadius = initR,
                                        LastRefresh = DateTime.UtcNow
                                    });


                                }
                            }

                            // 너무 오래 업데이트 안 된 트랙 제거(2초)
                            var now = DateTime.UtcNow;
                            _tracks.RemoveAll(t => (now - t.LastRefresh).TotalSeconds > 0.5);
                        }
                    }
                    finally
                    {
                        img.Dispose();
                        _detectBusy = false;
                    }
                });
            }

            // --- 4) prevGray 갱신 ---
            _prevGray?.Dispose();
            _prevGray = gray.Clone();

            // mat/gray는 using 또는 위에서 Clone/Dispose 처리
        }

        public void ClearPreview()
        {
            // UI에 잡고 있던 마지막 프레임 해제
            _ui.BeginInvoke(DispatcherPriority.Render, new Action(() => Preview = null));

            // 내부 버퍼도 해제
            _prevGray?.Dispose();
            _prevGray = null;
        }

        public void Dispose()
        {
            _camera.FrameArrived -= OnFrame;
            _camera.Dispose();
            _prevGray?.Dispose();
            // 녹화 리소스 정리(녹화 partial)
            Recording_Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
