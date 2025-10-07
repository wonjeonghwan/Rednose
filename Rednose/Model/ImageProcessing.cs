using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace APR_Rednose.Model
{
    public static class ImageProcessing
    {
        // Mat(BGR/GRAY/BGRA 등) -> WPF BitmapSource
        public static BitmapSource ToBitmapSource(Mat mat)
        {
            if (mat.Empty()) throw new ArgumentException("Empty Mat");
            return BitmapSourceConverter.ToBitmapSource(mat); // 안전한 복사 변환
        }

        // 파일에서 BitmapSource 로드(파일 잠금 방지)
        public static BitmapSource LoadBitmapSource(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // WPF BitmapSource -> Mat(BGR 3채널) 로 강제 변환
        public static Mat ToMat(BitmapSource src)
        {
            // 1) 항상 BGRA32로 맞춰 픽셀을 가져온다
            if (src.Format != System.Windows.Media.PixelFormats.Bgra32)
            {
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = src;
                converted.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
                converted.EndInit();
                converted.Freeze();
                src = converted;
            }

            int w = src.PixelWidth;
            int h = src.PixelHeight;
            int stride = w * 4;
            var pixels = new byte[h * stride];
            src.CopyPixels(pixels, stride, 0);

            // 2) BGRA 버퍼를 Mat(CV_8UC4)에 복사
            var bgra = new Mat(h, w, MatType.CV_8UC4);
            Marshal.Copy(pixels, 0, bgra.Data, pixels.Length);

            // 3) BGR(3채널)로 변환해서 반환
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            bgra.Dispose();
            return bgr;
        }

        public static void SaveBitmapSource(BitmapSource image, string path)
        {
            BitmapEncoder encoder;
            // 확장자로 포맷 자동 선택 (png/jpg만 우선)
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg") encoder = new JpegBitmapEncoder();
            else encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(image));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }
    }
}
