using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DlibDotNet;
using OpenCvSharp;

namespace APR_Rednose.Model
{
    public static class FaceDetection
    {
        private static readonly FrontalFaceDetector _detector = Dlib.GetFrontalFaceDetector();

        // dlib 네이티브 호출의 동시 접근을 막기 위한 전역 락
        private static readonly object _dlibLock = new object();

        public static OpenCvSharp.Rect[] DetectOpenCvRects(Mat bgr)
        {
            // BGR -> RGB
            using var rgb = new Mat();
            Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

            using var cont = rgb.IsContinuous() ? rgb : rgb.Clone();

            uint rows = (uint)cont.Rows;
            uint cols = (uint)cont.Cols;
            uint stride = (uint)cont.Step(); // bytes per row

            var bytes = new byte[stride * rows];
            Marshal.Copy(cont.Data, bytes, 0, bytes.Length);

            OpenCvSharp.Rect[] result;

            lock (_dlibLock)
            {
                // dlib 이미지 래핑 및 검출을 락 내부에서 완료,
                // 결과는 ToArray()로 복사하여 dimg Dispose 이후에도 사용
                using var dimg = Dlib.LoadImageData<RgbPixel>(bytes, rows, cols, stride);

                var dets = _detector.Operator(dimg);
                result = dets != null
                    ? dets.Select(r =>
                        new OpenCvSharp.Rect(
                            (int)r.Left,
                            (int)r.Top,
                            (int)r.Width,
                            (int)r.Height))
                      .ToArray()
                    : Array.Empty<OpenCvSharp.Rect>();
            }

            return result;
        }

        public static void DrawDetectionsInPlace(Mat bgr, OpenCvSharp.Rect[] rects)
        {
            foreach (var r in rects)
                Cv2.Rectangle(bgr, r, Scalar.Lime, 2, LineTypes.AntiAlias);
        }
    }
}
