using System.ComponentModel;

namespace RealmLauncher.Models
{
    public static class ModStatus
    {
        public const string UpToDate = "Актуален";
        public const string Outdated = "Устарел";
        public const string Missing = "Отсутствует";
        public const string Downloading = "Загрузка";
        public const string Done = "Готово";
        public const string Failed = "Ошибка";
    }

    public sealed class ModUpdateInfo : INotifyPropertyChanged
    {
        private string _status;
        private bool _isDragging;

        public bool IsDragging
        {
            get { return _isDragging; }
            set
            {
                if (_isDragging == value)
                {
                    return;
                }

                _isDragging = value;
                Raise("IsDragging");
            }
        }

        public string ModId { get; set; }
        public string PakName { get; set; }
        public long SizeBytes { get; set; }

        public string Status
        {
            get { return _status; }
            set
            {
                if (_status == value)
                {
                    return;
                }

                _status = value;
                Raise("Status");
                Raise("DisplayName");
            }
        }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PakName))
                {
                    return ModId;
                }

                return PakName.EndsWith(".pak", System.StringComparison.OrdinalIgnoreCase)
                    ? PakName.Substring(0, PakName.Length - 4)
                    : PakName;
            }
        }

        public string SizeText
        {
            get
            {
                if (SizeBytes <= 0)
                {
                    return string.Empty;
                }

                var mb = SizeBytes / 1024d / 1024d;
                return mb >= 1024
                    ? string.Format("{0:0.0} GB", mb / 1024d)
                    : string.Format("{0:0} MB", mb);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
