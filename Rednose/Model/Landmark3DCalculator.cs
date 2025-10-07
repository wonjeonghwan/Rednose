using System;
using OpenCvSharp;

namespace APR_Rednose.Model
{
    public static class Landmark3DCalculator
    {
        // solvePnP에 넣을 3D 기준점(단위 임의, 평균 얼굴 모델)
        // 순서: 0 Nose tip, 1 Chin, 2 Left eye outer, 3 Right eye outer, 4 Left mouth, 5 Right mouth
        public static Point3f[] GetModelPoints3D()
        {
            return new[]
            {
                new Point3f( 0.0f,   0.0f,   0.0f),   // nose tip
                new Point3f( 0.0f, -63.6f, -12.5f),  // chin
                new Point3f(-43.3f,  32.7f, -26.0f), // left eye outer corner
                new Point3f( 43.3f,  32.7f, -26.0f), // right eye outer corner
                new Point3f(-28.9f, -28.9f, -24.1f), // left mouth corner
                new Point3f( 28.9f, -28.9f, -24.1f)  // right mouth corner
            };
        }

        public static Mat GetCameraMatrix(Size imageSize, double fxFyScale = 1.0)
        {
            double fx = imageSize.Width * fxFyScale;
            double fy = imageSize.Width * fxFyScale;
            double cx = imageSize.Width / 2.0;
            double cy = imageSize.Height / 2.0;

            var cam = new Mat(3, 3, MatType.CV_64FC1);
            cam.Set(0, 0, fx); cam.Set(0, 1, 0); cam.Set(0, 2, cx);
            cam.Set(1, 0, 0); cam.Set(1, 1, fy); cam.Set(1, 2, cy);
            cam.Set(2, 0, 0); cam.Set(2, 1, 0); cam.Set(2, 2, 1);
            return cam;
        }

        public static (double yaw, double pitch, double roll) RvecToEuler(Mat rvec)
        {
            var R = new Mat();                   // ✅ out 대신 직접 Mat 만들어 전달
            Cv2.Rodrigues(rvec, R);

            double r00 = R.Get<double>(0, 0), r01 = R.Get<double>(0, 1), r02 = R.Get<double>(0, 2);
            double r10 = R.Get<double>(1, 0), r11 = R.Get<double>(1, 1), r12 = R.Get<double>(1, 2);
            double r20 = R.Get<double>(2, 0), r21 = R.Get<double>(2, 1), r22 = R.Get<double>(2, 2);

            // yaw-pitch-roll 근사
            double pitch = Math.Atan2(-r20, Math.Sqrt(r21 * r21 + r22 * r22)); // X
            double yaw = Math.Atan2(r10, r00);                           // Y
            double roll = Math.Atan2(r21, r22);                           // Z
            return (yaw, pitch, roll);
        }

        // imagePts: 6개 (nose/chin/eyeL/eyeR/mouthL/mouthR)
        public static bool SolvePose(Point2f[] imagePts, Size imgSize,
                             out Mat rvec, out Mat tvec,
                             out (double yaw, double pitch, double roll) ypr)
        {
            rvec = new Mat();
            tvec = new Mat();
            ypr = (0, 0, 0);

            if (imagePts == null || imagePts.Length != 6)
                return false;

            var objectPts = GetModelPoints3D();            // Point3f[6]
            using var cam = GetCameraMatrix(imgSize);
            using var dist = Mat.Zeros(4, 1, MatType.CV_64FC1); // 왜곡 0 가정

            // ✅ 배열 -> Mat 변환 (InputArray 대체)
            using var objMat = new Mat(objectPts.Length, 1, MatType.CV_32FC3);
            for (int i = 0; i < objectPts.Length; i++)
                objMat.Set(i, 0, objectPts[i]);           // Point3f 저장

            using var imgMat = new Mat(imagePts.Length, 1, MatType.CV_32FC2);
            for (int i = 0; i < imagePts.Length; i++)
                imgMat.Set(i, 0, imagePts[i]);            // Point2f 저장

            try
            {
                // 네 버전의 시그니처: void SolvePnP(InputArray, InputArray, InputArray, InputArray, OutputArray, OutputArray, ...)
                Cv2.SolvePnP(objMat, imgMat, cam, dist, rvec, tvec, false, SolvePnPFlags.Iterative);
            }
            catch
            {
                return false;
            }

            ypr = RvecToEuler(rvec);
            return true;
        }


    }
}
