// Services/RedNoseComposer.cs
using OpenCvSharp;
using System;

namespace APR_Rednose.Services
{
    public static class RedNoseComposer
    {
        public static class Sizing
        {
            // 기본값
            public static double RadiusFactor = 0.75;  // r ≈ RadiusFactor * 콧볼거리
            public static double MinFaceRatio = 0.05;  // r 하한 = 얼굴폭 * MinFaceRatio
            public static double MaxFaceRatio = 0.16;  // r 상한 = 얼굴폭 * MaxFaceRatio
            public static double AsymCapRatio = 1.70;  // 한쪽만 멀어질 때 과대확장 억제
            public static double DrawScale = 1.20;  // 크기
        }

        // r 계산만 필요할 때 사용(카메라/이미지 공용)
        public static double ComputeRadius(Point center, Point left, Point right, double? faceWidthHint = null)
        {
            // 콧볼 간 거리
            double distLR = Math.Sqrt((left.X - right.X) * (left.X - right.X) +
                                      (left.Y - right.Y) * (left.Y - right.Y));

            // 기본 반지름(통일 지점)
            double r = Sizing.RadiusFactor * distLR;

            // 얼굴폭 힌트가 있으면 얼굴크기 기반 클램프
            if (faceWidthHint.HasValue)
            {
                double minR = Sizing.MinFaceRatio * faceWidthHint.Value;
                double maxR = Sizing.MaxFaceRatio * faceWidthHint.Value;
                if (maxR < minR + 1) maxR = minR + 1;
                r = Math.Clamp(r, minR, maxR);
            }

            // 비대칭(한쪽 콧볼만 멀어짐) 캡
            double dL = Math.Sqrt((center.X - left.X) * (center.X - left.X) +
                                  (center.Y - left.Y) * (center.Y - left.Y));
            double dR = Math.Sqrt((center.X - right.X) * (center.X - right.X) +
                                  (center.Y - right.Y) * (center.Y - right.Y));
            double minD = Math.Min(dL, dR);
            double maxD = Math.Max(dL, dR);
            double asymCap = Math.Min(maxD, minD * Sizing.AsymCapRatio);

            // 최종 스케일 후 안전 하한
            r = Math.Min(r, asymCap * Sizing.DrawScale);
            return Math.Max(3.0, r);
        }

        // 그리기(권장): 좌표 + (선택)얼굴폭만 넘기면 알아서 사이징 후 그림
        public static void DrawSmart(Mat bgr, Point center, Point left, Point right, double? faceWidthHint = null)
        {
            int r = (int)Math.Round(ComputeRadius(center, left, right, faceWidthHint));

            // 본체
            Cv2.Circle(bgr, center, r, new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
            // 테두리(어두운 빨강)
            Cv2.Circle(bgr, center, r + 1, new Scalar(0, 0, 180), 2, LineTypes.AntiAlias);
            // 하이라이트(흰 점)
            var hl = new Point(center.X + (int)(r * 0.30), center.Y - (int)(r * 0.30));
            Cv2.Circle(bgr, hl, Math.Max(2, (int)(r * 0.18)), new Scalar(255, 255, 255), -1, LineTypes.AntiAlias);
        }

        // 반지름 지정 그리기(보간용)
        public static void DrawWithRadius(Mat bgr, Point center, double radius)
        {
            int r = Math.Max(3, (int)Math.Round(radius));

            // 본체
            Cv2.Circle(bgr, center, r, new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
            // 테두리
            Cv2.Circle(bgr, center, r + 1, new Scalar(0, 0, 180), 2, LineTypes.AntiAlias);
            // 하이라이트
            var hl = new Point(center.X + (int)(r * 0.30), center.Y - (int)(r * 0.30));
            Cv2.Circle(bgr, hl, Math.Max(2, (int)(r * 0.18)), new Scalar(255, 255, 255), -1, LineTypes.AntiAlias);
        }


        // (구버전 호환) 기존 DrawByExtents 호출이 남아있다면 내부적으로 DrawSmart로 위임
        public static void DrawByExtents(Mat bgr, Point center, Point left, Point right, double scale = 1.0, double capRatio = 1.50)
        {
            // scale, capRatio는 전역 Sizing으로 대체(일관성 유지)
            DrawSmart(bgr, center, left, right, null);
        }
    }
}
