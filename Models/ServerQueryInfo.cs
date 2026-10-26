namespace RealmLauncher.Models
{
    public sealed class ServerQueryInfo
    {
        public bool IsOnline { get; set; }
        public string Name { get; set; }
        public int Players { get; set; }
        public int MaxPlayers { get; set; }

        // A2S "visibility" byte: the server itself tells us whether it wants a password.
        public bool RequiresPassword { get; set; }
    }
}
