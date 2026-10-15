using BulkSwitchBackupPro.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace BulkSwitchBackupPro
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _viewModel.AppendLog("System", "Application Initialized. Welcome to Bulk Switch Backup Pro (C# Edition).");
            
            // Auto scroll logs
            var logsTextBox = (TextBox)FindName("LogTextBox");
            if (logsTextBox != null)
            {
                logsTextBox.TextChanged += (s, e) => logsTextBox.ScrollToEnd();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyDarkTitleBar();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private void ApplyDarkTitleBar()
        {
            if (Environment.OSVersion.Version.Major >= 10)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
                int trueValue = 1;
                // Try modern constant first
                int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
                if (result != 0) // Fallback for older Windows 10
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueValue, sizeof(int));
                }
            }
        }

        // Exposing passwords securely without data binding them to strings in memory longer than needed
        public string GetPassword() => PasswordBox.Password;
        public string GetSecret() => SecretBox.Password;
    }
}