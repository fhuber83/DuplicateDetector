namespace DuplicateDetectorCore
{
    public class DuplicateFileInfo
    {
        public string? FileName { get; set; }
        public string? Path { get; set; }
        public string? Hash { get; set; }
        public int Count { get; set; } = 1;
        public long FileSize { get; set; } = 0;
        public string FileSizeReadable { get => Util.GetReadableSizeString(FileSize); }

        public DateTime? LastChange { get; set; }
        public DateTime? CreationTime { get; set; }
    }
}