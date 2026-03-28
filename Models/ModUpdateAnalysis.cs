using System.Collections.Generic;
using System.Linq;

namespace RealmLauncher.Models
{
    public sealed class ModUpdateAnalysis
    {
        public List<ModUpdateInfo> Updates { get; set; }

        public ModUpdateAnalysis()
        {
            Updates = new List<ModUpdateInfo>();
        }

        public List<string> UniqueModIdsToUpdate()
        {
            return Updates
                .Where(x => !string.IsNullOrWhiteSpace(x.ModId))
                .Select(x => x.ModId)
                .Distinct()
                .ToList();
        }

        public long TotalSizeBytes()
        {
            return Updates.Sum(x => x.SizeBytes);
        }
    }
}
