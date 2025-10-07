using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using APR_Rednose.Services;

namespace APR_Rednose.ViewModel
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public CameraViewModel CameraVM { get; }
        public PhotoInputViewModel PhotoVM { get; }

        // Home / Camera / Image
        private string _currentPage = "Camera";
        public string CurrentPage
        {
            get => _currentPage;
            private set { _currentPage = value; OnPropertyChanged(); }
        }

        public MainViewModel(ICameraService cameraService)
        {
            CameraVM = new CameraViewModel(cameraService);
            PhotoVM = new PhotoInputViewModel();

            // 시작 페이지
            CurrentPage = "Camera";
            // 카메라 자동 시작
            Application.Current.Dispatcher.BeginInvoke(() => CameraVM.StartCommand.Execute(null));
        }


        public void GoHome()
        {
            if (CurrentPage == "Camera")
            {
                if (CameraVM.IsRecording) CameraVM.StopRecordingCommand.Execute(null);
                CameraVM.StopCommand.Execute(null);
                CameraVM.ClearPreview();
            }
            CurrentPage = "Home";
        }


        public void GoCamera()
        {
            // 0) Image 탭 잔상 제거
            PhotoVM.Clear();
            CurrentPage = "Camera";
            // 들어오자마자 카메라 자동 시작
            Application.Current.Dispatcher.BeginInvoke(() => CameraVM.StartCommand.Execute(null));
        }

        public void GoImage()
        {
            CameraVM.StopCommand.Execute(null);
            CameraVM.ClearPreview();         
            CurrentPage = "Image";
            Application.Current.Dispatcher.BeginInvoke(() => PhotoVM.OpenCommand.Execute(null));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
