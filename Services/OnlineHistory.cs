using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RealmLauncher.Services
{
    public sealed class OnlineSample
    {
        [JsonProperty("t")]
        public DateTime TimeUtc { get; set; }

        [JsonProperty("p")]
        public int Players { get; set; }
    }

    public sealed class OnlineHistory
    {
        private static readonly TimeSpan Window = TimeSpan.FromHours(24);
        private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(25);

        private readonly string _path;
        private readonly List<OnlineSample> _samples = new List<OnlineSample>();

        public OnlineHistory()
        {
            _path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RealmLauncher",
                "online-history.json");

            Load();
        }

        public IReadOnlyList<OnlineSample> Samples
        {
            get { return _samples; }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var json = File.ReadAllText(_path);
                var loaded = JsonConvert.DeserializeObject<List<OnlineSample>>(json);
                if (loaded == null)
                {
                    return;
                }

                _samples.AddRange(loaded);
                Trim();
            }
            catch
            {
                _samples.Clear();
            }
        }

        public void Add(int players)
        {
            var now = DateTime.UtcNow;

            if (_samples.Count > 0 && now - _samples[_samples.Count - 1].TimeUtc < MinimumGap)
            {
                return;
            }

            _samples.Add(new OnlineSample { TimeUtc = now, Players = Math.Max(0, players) });
            Trim();
            Save();
        }

        private void Trim()
        {
            var cutoff = DateTime.UtcNow - Window;
            _samples.RemoveAll(s => s.TimeUtc < cutoff);

            if (_samples.Count > 4000)
            {
                _samples.RemoveRange(0, _samples.Count - 4000);
            }
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_path, JsonConvert.SerializeObject(_samples));
            }
            catch
            {
            }
        }

        public int PeakPlayers()
        {
            return _samples.Count == 0 ? 0 : _samples.Max(s => s.Players);
        }
    }
}
