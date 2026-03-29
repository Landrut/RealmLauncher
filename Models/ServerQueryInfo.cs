namespace RealmLauncher.Models
{
    public sealed class ServerQueryInfo
    {
        public bool IsOnline { get; set; }
        public string Name { get; set; }
        public int Players { get; set; }
        public int MaxPlayers { get; set; }
    }
}
