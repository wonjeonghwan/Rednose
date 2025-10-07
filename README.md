# RedNose

> 실시간 카메라 기반의 **얼굴·코 인식 이펙트 합성 프로그램**
> WPF MVVM 구조로 개발되었으며, CAMERA/IMAGE 모드를 통해 화면 녹화,캡처 및 이미지 합성 기능을 지원

## 실행방법
1. Visual Studio 2022로 `Rednose.sln` 열기
2. NuGet 패키지 복원
   - 솔루션 탐색기에서 우클릭 → [NuGet 패키지 복원]
   - 또는 터미널에서 `dotnet restore`
3. `Resources/Assets/shape_predictor_68_face_landmarks.dat` 위치 확인
4. F5 (디버그 실행) 또는 Ctrl+F5 (디버그 없이 실행)

### Camera 모드
- 카메라를 통해 실시간으로 코에 이미지가 합성된 모습이 보여짐 <br>
**상단 버튼 설명**<br>
**Capture** : 현재 화면 캡쳐 <br>
**REC** : 화면 녹화<br>
**STOP** : 녹화 중지<br>
**FLIP** : 좌우반전<br>

### IMAGE 모드
- 이미지 파일 내에 얼굴 위치를 파악하여 코에 루돌프 코 이미지가 합성됨<br>
**상단 버튼 설명**<br>
**OPEN** : 이미지 불러오기, 불러온 이미지에서 코 위치가 합성되어 화면에 띄워짐<br>
**SAVE** : 합성한 이미지 저장<br>

<img width="1085" height="692" alt="image" src="https://github.com/user-attachments/assets/88386622-b915-4ad8-9608-09d71d1aef2d" />
<img width="1091" height="693" alt="image" src="https://github.com/user-attachments/assets/8c831611-69a8-45af-a058-67a07b4eb817" />


## 참여 인원
원정환 – Vision 파이프라인 구현, WPF UI, MVVM 구조 설계 및 통합

## 사용 기술
| 구분        | 기술/라이브러리 |
|-------------|----------------|
| Framework   | .NET 6.0, WPF (XAML, MVVM)  |
| Vision      | OpenCvSharp4, DlibDotNet |
| UI          | WPF XAML |

## 프로젝트 개요
- 카메라 프레임을 실시간 수신하고, 얼굴 검출 + 코 위치 추적을 수행합니다.
- 사진 모드와 동영상 녹화/캡처 기능을 함께 지원합니다.
- 영상 내 합성시 코 이미지의 떨림현상을 최소화.
- MVVM 구조로 확장성을 향상.

## 구현 내용
### 1. 앱 구조 설계 (WPF + MVVM)
- Home/Camera/Image -> **CameraView, PhotoInputView 2개 페이지로 변경 구성**
- MainViewModel으로 라우팅 : Camera/Image 페이지 변경
- RelayCommand로 버튼 이벤트를 ViewModel에 바인딩
- PageToVisibilityConverter로 페이지 전환 구현

### 2. 비전 파이프라인 구현
- 웹캠 입력: ICameraService → OpenCv VideoCapture 이벤트로 프레임 수신
- 얼굴 검출: OpenCV Haar 기반 bbox 검출, 
- 랜드마크 검출: Dlib shape_predictor_68_face_landmarks.dat로 Nose Tip / Nostril 추출
- 검출주기 : 50ms
- 옵티컬 플로우 추적: Cv2.CalcOpticalFlowPyrLK로 프레임 간 Nose/Nostril 위치 보정
- EMA로 좌표와 반지름 값 안정화
- Deadband + 최대 증감폭 제한으로 떨림 억제
