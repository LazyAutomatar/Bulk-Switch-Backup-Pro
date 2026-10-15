using CommunityToolkit.Mvvm.ComponentModel;

namespace BulkBackup.Core.Models
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

        public string MaskedPassword => new string('•', System.Math.Max(6, Password?.Length ?? 6));
        public bool HasSecret => !string.IsNullOrEmpty(Secret);
    }
}
