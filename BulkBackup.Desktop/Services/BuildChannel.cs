namespace BulkBackup.Desktop.Services;

public static class BuildChannel
{
#if LOCAL_BETA
    public static bool IsLocalBeta => true;
#else
    public static bool IsLocalBeta => false;
#endif
}
