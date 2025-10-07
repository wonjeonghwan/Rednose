using System.Windows;
using APR_Rednose.Services;
using APR_Rednose.ViewModel;
using System.Windows.Input;


namespace APR_Rednose.View
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            var camera = new OpenCVCameraService();
            _vm = new MainViewModel(camera);
            DataContext = _vm;

            this.Closed += (_, __) => _vm.CameraVM.Dispose();
        }
        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { this.DragMove(); } catch { /* 드래그 중 예외 무시 */ }
            }
        }

        private void OnGoCamera(object sender, RoutedEventArgs e) => _vm.GoCamera();
        private void OnGoImage(object sender, RoutedEventArgs e) => _vm.GoImage();
        private void OnBack(object sender, RoutedEventArgs e) => this.Close();

    }
}
