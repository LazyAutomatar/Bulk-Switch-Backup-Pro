using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace BulkSwitchBackupPro.Models
{
    public partial class DeviceModel : ObservableObject
    {
        [ObservableProperty]
        private string ipAddress = string.Empty;

        [ObservableProperty]
        private string hostname = string.Empty;

        [ObservableProperty]
        private string deviceType = "Switch"; // "Switch" or "Access Point"

        [ObservableProperty]
        private string status = "Pending";

        [ObservableProperty]
        private int progress = 0;

        [ObservableProperty]
        private string protocol = "Waiting...";

        [ObservableProperty]
        private SolidColorBrush protocolColor = new(Colors.LightGray);

        [ObservableProperty]
        private SolidColorBrush statusColor = new(Color.FromRgb(60, 60, 65));

        [ObservableProperty]
        private SolidColorBrush statusTextColor = new(Colors.White);

        [ObservableProperty]
        private bool isCompleted = false;

        [ObservableProperty]
        private bool isFailed = false;
    }
}
