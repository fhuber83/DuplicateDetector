using DuplicateDetectorCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DuplicateDetectorCore
{
    public class HashSummaryItem
    {
        public List<DuplicateFileInfo> Files { get; set; } = new List<DuplicateFileInfo>();
        public long TotalSize { get; set; } = 0L;
        public string TotalSizeReadable { get => Util.GetReadableSizeString(TotalSize); }
        public string FileNames { get; set; }
    }
}
