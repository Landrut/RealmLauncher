using System.Collections.Generic;
using System.Linq;

namespace RealmLauncher.Models
{
    public sealed class ModUpdateAnalysis
    {
        public List<ModUpdateInfo> All { get; set; }

        public List<ModUpdateInfo> Updates { get; set; }

        public ModUpdateAnalysis()
        {
            All = new List<ModUpdateInfo>();
            Updates = new List<ModUpdateInfo>();
        }

        public long TotalSizeBytes()
        {
            return Updates.Sum(x => x.SizeBytes);
        }
    }
}
