using CommunityToolkit.Mvvm.ComponentModel;

namespace BulkBackup.Core.Models
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
        private string uplinkDevice = string.Empty;

        [ObservableProperty]
        private string port = string.Empty;

        [ObservableProperty]
        private string status = "Queued";

        [ObservableProperty]
        private int progress = 0;

        [ObservableProperty]
        private string protocol = "Waiting...";

        [ObservableProperty]
        private string protocolColor = "#888888";

        [ObservableProperty]
        private string statusColor = "#2D2D32";

        [ObservableProperty]
        private string statusTextColor = "#FFFFFF";

        [ObservableProperty]
        private bool isCompleted = false;

        [ObservableProperty]
        private bool isFailed = false;

        [ObservableProperty]
        private bool isInProgress = false;
    }
}
