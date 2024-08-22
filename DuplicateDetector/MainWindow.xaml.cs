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
using DuplicateDetectorCore;

namespace DuplicateDetector
{
    public partial class MainWindow : Window
    {
        public DuplicateDetectorCore.DuplicateDetector CurrentSession { get; set; } = null;

        public MainWindow()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            Title += $" v{version.Major}.{version.Minor}.{version.Build}";
        }


        private void ProcessFile(string path, List<DuplicateFileInfo> fileItems, Dictionary<string, HashSummaryItem> hashMap)
        {
            try
            {
                var hashString = DuplicateDetectorCore.DuplicateDetector.CalculateHash(path);

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

        private CancellationTokenSource? CancellationTokenSource = null;


        private async void ListViewFiles_Drop(object sender, DragEventArgs e)
        {
            if (CancellationTokenSource != null)
                return;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            CancellationTokenSource = new CancellationTokenSource();

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

            bool addToSession = Keyboard.GetKeyStates(Key.LeftCtrl).HasFlag(KeyStates.Down);

            var files = (string[]) e.Data.GetData(DataFormats.FileDrop);

            var duplicateDetector = new DuplicateDetectorCore.DuplicateDetector();
            await duplicateDetector.ProcessDirectoryAsync(files, addToSession, CancellationTokenSource.Token, (stage, percentage, total) =>
            {
                string? str = null;

                switch(stage)
                {
                    case DuplicateDetectorCore.DuplicateDetector.ProcessingStage.Enumerating:
                        str = $"Enumerating files ({total.Value} found)";
                        break;

                    case DuplicateDetectorCore.DuplicateDetector.ProcessingStage.Processing:
                        str = $"Processing ({percentage:F1}%)";
                        break;

                    case DuplicateDetectorCore.DuplicateDetector.ProcessingStage.PostProcessing:
                        str = $"Post-Processing ({percentage:F1}%)";
                        break;

                    case DuplicateDetectorCore.DuplicateDetector.ProcessingStage.Done:
                        str = "Done";
                        break;

                    case DuplicateDetectorCore.DuplicateDetector.ProcessingStage.Cancelled:
                        str = "Cancelled";
                        break;
                }

                //StatusBarItem1.Dispatcher.BeginInvoke(() => StatusBarItem1.Content = str ?? "Unknown");
                statusLabel.Dispatcher.BeginInvoke(() => statusLabel.Content = str ?? "Unknown");
                if(percentage.HasValue)
                {
                    progressBar.Dispatcher.BeginInvoke(() => progressBar.Value = percentage.Value);
                }
            });

            if (addToSession && CurrentSession != null)
            {
                CurrentSession.MergeWith(duplicateDetector);
                CollectionViewSource.GetDefaultView(ListViewFiles.ItemsSource).Refresh();
            }
            else
            {
                CurrentSession = duplicateDetector;
                ListViewFiles.ItemsSource = CurrentSession.HashMap;
            }

            CancellationTokenSource = null;

            StatusBarItem1.Content = oldContent;
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