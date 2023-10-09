using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DuplicateDetectorCore
{
    public class Util
    {
        public static string GetReadableSizeString(long size)
        {
            int unitIndex = 0;

            var units = new string[] { "B", "KB", "MB", "GB", "TB" };

            double _size = size;

            while (_size >= 1024.0 && unitIndex < (units.Length - 1))
            {
                _size /= 1024.0;
                unitIndex += 1;
            }

            return $"{_size:F2} {units[unitIndex]}";
        }
    }
}
