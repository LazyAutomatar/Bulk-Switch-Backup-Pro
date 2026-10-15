using CommunityToolkit.Mvvm.ComponentModel;

namespace BulkSwitchBackupPro.Models
{
    public partial class CredentialEntry : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string secret = string.Empty;

        public string MaskedPassword => new string('•', System.Math.Max(6, Password.Length));
        public bool HasSecret => !string.IsNullOrEmpty(Secret);
    }
}
