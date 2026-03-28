namespace RealmLauncher.Models
{
    public sealed class LauncherUpdateManifest
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public long? SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public string Changelog { get; set; }
    }
}
