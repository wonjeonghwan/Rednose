using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DlibDotNet;
using OpenCvSharp;

namespace APR_Rednose.Services
{
    public static class FaceLandmarks
    {
        private static readonly object _lock = new();
        private static ShapePredictor? _sp;

        private static void EnsureLoaded(string modelPath)
        {
            if (_sp != null) return;
            lock (_lock)
            {
                if (_sp == null)
                {
                    if (!System.IO.File.Exists(modelPath))
                        throw new InvalidOperationException($"Landmark model not found: {modelPath}");
                    _sp = ShapePredictor.Deserialize(modelPath);
                }
            }
        }

        // OpenCV BGR Mat -> Dlib Array2D<RgbPixel>
        private static Array2D<RgbPixel> MatToDlibRgb(Mat bgr, out uint rows, out uint cols, out uint stride, out byte[] buffer)
        {
            using var rgb = new Mat();
            Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

            using var cont = rgb.IsContinuous() ? rgb : rgb.Clone();
            rows = (uint)cont.Rows;
            cols = (uint)cont.Cols;
            stride = (uint)cont.Step();
            buffer = new byte[stride * rows];
            Marshal.Copy(cont.Data, buffer, 0, buffer.Length);
            return Dlib.LoadImageData<RgbPixel>(buffer, rows, cols, stride);
        }

        public struct NoseResult
        {
            public OpenCvSharp.Point NoseTip;
            public OpenCvSharp.Point LeftNostril;
            public OpenCvSharp.Point RightNostril;
            public OpenCvSharp.Point[] All; // 68개 전부 (원하면 디버그용)
        }

        public static NoseResult? DetectNose(Mat bgr, OpenCvSharp.Rect face, string modelPath)
        {
            EnsureLoaded(modelPath);

            using var dimg = MatToDlibRgb(bgr, out var rows, out var cols, out var stride, out var _);

            // Dlib Rectangle은 (left, top, right, bottom) = 끝점 포함
            var drect = new DlibDotNet.Rectangle(face.Left, face.Top, face.Right - 1, face.Bottom - 1);

            var shape = _sp!.Detect(dimg, drect);

            // 68 포인트 수집
            var pts = new List<OpenCvSharp.Point>(68);
            for (uint i = 0; i < shape.Parts; i++)
            {
                var p = shape.GetPart(i);
                pts.Add(new OpenCvSharp.Point(p.X, p.Y));
            }

            // Dlib 68 인덱스(0-based): 30=코끝, 31=왼콧볼, 35=오른콧볼
            var noseTip = pts[30];
            var left = pts[31];
            var right = pts[35];

            return new NoseResult { NoseTip = noseTip, LeftNostril = left, RightNostril = right, All = pts.ToArray() };
        }
    }
}
