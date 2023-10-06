using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.IO;
using System.Windows.Media.TextFormatting;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Policy;
using System.Reflection;

namespace DuplicateDetector
{
    public partial class MainWindow : Window
    {
        public class HashSummaryItem
        {
            public List<DuplicateFileInfo> Files { get; set; } = new List<DuplicateFileInfo>();
            public long TotalSize { get; set; } = 0L;
            public string TotalSizeReadable { get => Util.GetReadableSizeString(TotalSize); }
            public string FileNames { get; set; }
        }

        public Dictionary<String, HashSummaryItem>? HashMap = null;

        public List<DuplicateFileInfo>? FileItems = null;


        public MainWindow()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            Title += $" v{version.Major}.{version.Minor}.{version.Build}";
        }

        #region Hash calculation
        const long blockSize = 1024L * 1024L * 256L; // Number of bytes to process at a time

        private string CalculateHash(string path)
        {
            SHA1 sha1 = SHA1.Create();

            var fileInfo = new System.IO.FileInfo(path);

            // Process entire file at once
            if (fileInfo.Length <= blockSize)
            {
                sha1.ComputeHash(File.ReadAllBytes(path));
            }
            else
            {
                var buffer = new byte[blockSize];

                var fileStream = File.OpenRead(path);

                long sizeRemaining = fileInfo.Length;

                while (sizeRemaining > 0L)
                {
                    long sizeToRead = sizeRemaining < blockSize ? sizeRemaining : blockSize;

                    int bytesRead = fileStream.Read(buffer, 0, (int)sizeToRead);

                    sha1.ComputeHash(buffer, 0, bytesRead);

                    sizeRemaining -= sizeToRead;
                }
            }

            if (sha1.Hash is null)
                throw new Exception("Unable to calculate hash");

            var sb = new StringBuilder();

            foreach (var b in sha1.Hash)
            {
                sb.Append($"{b:X2}");
            }

            return sb.ToString();
        }
        #endregion


        private void ProcessFile(string path, List<DuplicateFileInfo> fileItems, Dictionary<string, HashSummaryItem> hashMap)
        {
            try
            {
                var hashString = CalculateHash(path);

                var fileInfo = new FileInfo(path);

                var item = new DuplicateFileInfo
                {
                    FileName = System.IO.Path.GetFileName(path),
                    Path = System.IO.Path.GetDirectoryName(path),
                    Hash = hashString,
                    FileSize = fileInfo.Length,
                    LastChange = fileInfo.LastWriteTime,
                    CreationTime = fileInfo.CreationTime
                };

                // We have a possible duplicate
                if (hashMap.ContainsKey(hashString))
                {
                    var duplicateCount = 1; // Count this file, too

                    foreach (var duplicate in hashMap[hashString].Files)
                    {
                        duplicateCount++;
                    }

                    hashMap[hashString].Files.Add(item);

                    foreach (var dupItem in hashMap[hashString].Files)
                    {
                        dupItem.Count = duplicateCount;
                    }
                }

                // First file with this hash
                else
                {
                    hashMap.Add(hashString, new HashSummaryItem());

                    hashMap[hashString].Files.Add(item);
                }

                fileItems.Add(item);
            }
            catch (Exception)
            {

            }
        }

        private void ProcessFile(string path, ConcurrentBag<DuplicateFileInfo> list)
        {
            try
            {
                SHA1 sha1 = SHA1.Create();

                sha1.ComputeHash(File.ReadAllBytes(path));

                if (sha1.Hash is not null)
                {
                    var hashString = CalculateHash(path);

                    var fileInfo = new FileInfo(path);

                    var item = new DuplicateFileInfo
                    {
                        FileName = System.IO.Path.GetFileName(path),
                        Path = System.IO.Path.GetDirectoryName(path),
                        Hash = hashString,
                        FileSize = fileInfo.Length,
                        LastChange = fileInfo.LastWriteTime,
                        CreationTime = fileInfo.CreationTime
                    };

                    list.Add(item);
                }
            }
            catch (Exception)
            {
            }
        }



        private void ListViewItemDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is ListViewItem lvi)
            {
                if (lvi.Content is KeyValuePair<string, HashSummaryItem> item)
                {
                    var dlg = new DuplicateDetailsWindow(item.Key, item.Value.Files)
                    {
                        Owner = this
                    };

                    dlg.ShowDialog();

                    CollectionViewSource.GetDefaultView(ListViewFiles.ItemsSource).Refresh();
                }
            }
        }


        private void ListViewFiles_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    foreach (var file in files)
                    {
                        if (!File.GetAttributes(file).HasFlag(FileAttributes.Directory))
                        {
                            e.Effects = DragDropEffects.None;
                            return;
                        }
                    }

                    // All paths are directories
                    e.Effects = DragDropEffects.All;
                }
            }
        }

        private void GetFileNamesRecursively(string rootPath, List<string> list, CancellationToken token)
        {
            if (!Directory.Exists(rootPath))
                throw new ArgumentException("Must be an existing directory", nameof(rootPath));

            var directoriesToDo = new Stack<string>();

            directoriesToDo.Push(rootPath);

            while (directoriesToDo.Count > 0)
            {
                if (token.IsCancellationRequested)
                    break;

                var path = directoriesToDo.Pop();

                try
                {
                    foreach (var subDirectory in Directory.GetDirectories(path))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        directoriesToDo.Push(subDirectory);
                    }

                    foreach (var file in Directory.GetFiles(path))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        list.Add(file);
                    }
                }
                catch (Exception)
                {
                    // Ignore, probably unauthorized to access this folder
                }
            }
        }

        private CancellationTokenSource? CancellationTokenSource = null;


        private async void ListViewFiles_Drop(object sender, DragEventArgs e)
        {
            if (CancellationTokenSource != null)
                return;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                CancellationTokenSource = new CancellationTokenSource();

                ListViewFiles.AllowDrop = false;

                var oldContent = StatusBarItem1.Content;

                var progressBar = new ProgressBar() { Width = 100 };
                var statusLabel = new Label { Content = "Processing (Enumerating files)..." };
                var linkCancel = new Hyperlink(new Run("Click to cancel"));
                linkCancel.Click += (o, e) => { CancellationTokenSource.Cancel(); };
                var layout = new StackPanel { Orientation = Orientation.Horizontal };
                layout.Children.Add(progressBar);
                layout.Children.Add(statusLabel);
                layout.Children.Add(new Label { Content = linkCancel });
                StatusBarItem1.Content = layout;

                var timeBefore = DateTime.UtcNow;

                var result = await Task.Run(() =>
                {
                    var fileInfos = new List<DuplicateFileInfo>();
                    var hashMap = new Dictionary<string, HashSummaryItem>();
                    long totalSize = 0L;

                    // Create a list of all files to check
                    var listOfFiles = new List<string>();

                    foreach (var file in files)
                    {
                        if (CancellationTokenSource.IsCancellationRequested)
                        {
                            return (false, null, null, 0L);
                        }

                        if (Directory.Exists(file))
                        {
                            GetFileNamesRecursively(file, listOfFiles, CancellationTokenSource.Token);
                        }
                        else if (File.Exists(file))
                        {
                            listOfFiles.Add(file);
                        }
                    }

                    int maxCount = listOfFiles.Count;

                    progressBar.Dispatcher.Invoke(() =>
                    {
                        progressBar.Minimum = 0;
                        progressBar.Maximum = maxCount;
                        progressBar.Value = 0;
                    });

                    int count = 0;

                    // Calculate hashes in parallel
                    var infoBag = new ConcurrentBag<DuplicateFileInfo>();

                    ParallelOptions parallelOptions = new()
                    {
                        CancellationToken = CancellationTokenSource.Token,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    };

                    try
                    {
                        Parallel.ForEach(listOfFiles, parallelOptions, file =>
                        {
                            ProcessFile(file, infoBag);

                            Interlocked.Increment(ref count);

                            progressBar.Dispatcher.Invoke(() =>
                            {
                                progressBar.Value = count;
                                statusLabel.Content = $"Processing {count} / {maxCount}...";
                            });
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        return (false, null, null, 0L);
                    }

                    // Build duplicate dictionary
                    progressBar.Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = maxCount;
                        statusLabel.Content = "Post-Processing...";
                    });


                    foreach (var info in infoBag)
                    {
                        // Copy item from bag to List
                        fileInfos.Add(info);

                        totalSize += info.FileSize;

                        // Hash already in map?
                        if (!hashMap.ContainsKey(info.Hash))
                        {
                            // No, create new entry for this hash
                            hashMap.Add(info.Hash, new HashSummaryItem());
                        }

                        // Add this item to the hash map
                        hashMap[info.Hash].Files.Add(info);

                        // Set each item's "number of duplicate" counter
                        foreach (var item in hashMap[info.Hash].Files)
                        {
                            item.Count = hashMap[info.Hash].Files.Count;
                        }

                        var distinctNames = hashMap[info.Hash].Files.Select(x => x.FileName).Distinct().ToList();

                        {
                            var sb = new StringBuilder();

                            for (int i = 0; i < distinctNames.Count; i++)
                            {
                                sb.Append(distinctNames[i]);
                                
                                if(i < (distinctNames.Count - 1))
                                {
                                    sb.Append(", ");
                                }
                            }

                            hashMap[info.Hash].FileNames = sb.ToString();
                        }

                        // Calculate the total number of bytes used for each hash
                        try
                        {
                            Parallel.ForEach(hashMap.Keys, parallelOptions, hash =>
                            {
                                long total = 0;

                                foreach (var item in hashMap[hash].Files)
                                {
                                    total += item.FileSize;
                                }

                                hashMap[hash].TotalSize = total;
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            return (false, null, null, 0L);
                        }
                    }

                    return (true, fileInfos, hashMap, totalSize);
                });

                var timeAfter = DateTime.UtcNow;

                var seconds = (timeAfter - timeBefore).TotalSeconds;

                if (result.Item1)
                {
                    HashMap = result.hashMap;

                    foreach (var hash in HashMap.Keys.ToList())
                    {
                        if (HashMap[hash].Files.Count == 1)
                        {
                            HashMap.Remove(hash);
                        }
                    }

                    ListViewFiles.ItemsSource = HashMap;

                    SetInitialSort();

                    double speed = (result.Item4 / (1024.0 * 1024.0)) / seconds;

                    StatusLabel.Content = $"{Util.GetReadableSizeString(result.Item4)} processed in {seconds:F2} seconds ({speed:F1} MB/s).";
                }

                CancellationTokenSource = null;

                ListViewFiles.AllowDrop = true;
                StatusBarItem1.Content = oldContent;
            }
        }


        #region Sorting
        GridViewColumnHeader _lastHeaderClicked = null;
        ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private void Sort(string sortBy, ListSortDirection direction)
        {
            if (sortBy == "Value.TotalSizeReadable")
            {
                sortBy = "Value.TotalSize";
            }

            ICollectionView dataView =
              CollectionViewSource.GetDefaultView(ListViewFiles.ItemsSource);

            dataView.SortDescriptions.Clear();
            SortDescription sd = new SortDescription(sortBy, direction);
            dataView.SortDescriptions.Add(sd);
            dataView.Refresh();
        }

        private void SetInitialSort()
        {
            Sort("Value.Files.Count", ListSortDirection.Descending);

            _lastHeaderClicked = ColumnHeaderForCount;
            _lastDirection = ListSortDirection.Descending;
        }

        void GridViewColumnHeaderClickedHandler(object sender, RoutedEventArgs e)
        {
            var headerClicked = e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction;

            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Descending;
                    }
                    else
                    {
                        if (_lastDirection == ListSortDirection.Ascending)
                        {
                            direction = ListSortDirection.Descending;
                        }
                        else
                        {
                            direction = ListSortDirection.Ascending;
                        }
                    }

                    var columnBinding = headerClicked.Column.DisplayMemberBinding as Binding;
                    var sortBy = columnBinding?.Path.Path ?? headerClicked.Column.Header as string;

                    Sort(sortBy, direction);

                    if (direction == ListSortDirection.Ascending)
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    }
                    else
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplateArrowDown"] as DataTemplate;
                    }

                    // Remove arrow from previously sorted header
                    if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
                    {
                        _lastHeaderClicked.Column.HeaderTemplate = null;
                    }

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }
        #endregion

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            CancellationTokenSource?.Cancel();
        }
    }
}