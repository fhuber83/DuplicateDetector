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
using System.Diagnostics.Eventing.Reader;

namespace DuplicateDetector
{
    public partial class MainWindow : Window
    {
        public DuplicateDetectorCore.DuplicateDetector CurrentSession { get; set; } = null;

        public bool ShowAll { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            Title += $" v{version.Major}.{version.Minor}.{version.Build}";
            
            CheckBoxShowAll.IsChecked = ShowAll;
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

        private Dictionary<string, HashSummaryItem> DisplayedFiles = null;

        private void UpdateFileList()
        {
            if (CurrentSession is null || CurrentSession.HashMap is null)
                return;

            var duplicates = new Dictionary<string, HashSummaryItem>();

            foreach (var hash in CurrentSession.HashMap.Keys)
            {
                if (CurrentSession.HashMap[hash].Files.Count > 1 || ShowAll)
                {
                    duplicates.Add(hash, CurrentSession.HashMap[hash]);
                }
            }

            DisplayedFiles = duplicates;
            ListViewFiles.ItemsSource = DisplayedFiles;
            CollectionViewSource.GetDefaultView(ListViewFiles.ItemsSource).Refresh();
        }


        private async void ListViewFiles_Drop(object sender, DragEventArgs e)
        {
            if (CancellationTokenSource != null)
                return;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            CancellationTokenSource = new CancellationTokenSource();

            ContextMenuMainList.IsEnabled = false;

            var timeBefore = DateTime.UtcNow;

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
            await duplicateDetector.ProcessDirectoryAsync(files, keepSingleFiles: true, CancellationTokenSource.Token, (stage, percentage, total) =>
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

                statusLabel.Dispatcher.Invoke(() => statusLabel.Content = str ?? "Unknown");
                if(percentage.HasValue)
                {
                    progressBar.Dispatcher.Invoke(() => progressBar.Value = percentage.Value);
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
            }

            UpdateFileList();

            ContextMenuMainList.IsEnabled = true;

            CancellationTokenSource = null;

            if (CurrentSession != null && CurrentSession.HashMap != null)
            {

                int numDuplicates = 0;
                long duplicateBytes = 0;

                foreach (var item in CurrentSession.HashMap.Values)
                {
                    if (item.Files.Count > 1)
                    {
                        numDuplicates += item.Files.Count - 1;
                        duplicateBytes += item.TotalSize;
                    }
                }

                var timeAfter = DateTime.UtcNow;
                var timeTaken = timeAfter - timeBefore;
                var MegsProcessed = duplicateDetector.TotalSize / (1024.0 * 1024.0);
                var MegsPerSecond = MegsProcessed / timeTaken.TotalSeconds;

                if (numDuplicates > 0)
                {
                    StatusBarItem1.Content = $"Done. Found {DuplicateDetectorCore.Util.GetReadableSizeString(duplicateBytes)} in {numDuplicates} duplicate{(numDuplicates == 1 ? "" : "s")}. {MegsPerSecond:F1} MB/s";
                }
                else
                {
                    StatusBarItem1.Content = $"Done. No duplicates found. {MegsPerSecond:F1} MB/s";
                }

                StatusBarItem1.ToolTip = $"Processed {MegsProcessed:F1} MB in {timeTaken.TotalSeconds:F1} seconds ({MegsPerSecond:F1} MB/s)";
            }
            else
            {
                StatusBarItem1.Content = "Cancelled";
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

        private void CheckBoxShowAll_Checked(object sender, RoutedEventArgs e)
        {
            ShowAll = CheckBoxShowAll.IsChecked!.Value;
            UpdateFileList();
        }

        private async void MenuItemAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Multiselect = true
            };

            if(dlg.ShowDialog() == true)
            {
                foreach(var folder in dlg.FolderNames)
                {
                }
            }
        }

        private void CheckBoxShowHashes_Checked(object sender, RoutedEventArgs e)
        {
            ListColumnHash.Width = CheckBoxShowHashes.IsChecked == true ? 300 : 0;
        }

        private void MenuItemClearSession_Click(object sender, RoutedEventArgs e)
        {
            if(CurrentSession?.HashMap?.Count > 0)
            {
                if (MessageBox.Show(this, "Clear list and start a new session?", "DuplicateDetector", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                {
                    ListViewFiles.ItemsSource = null;
                    CurrentSession = null;
                    DisplayedFiles = null;
                    StatusBarItem1.Content = "Session cleared";
                }
            }
        }
    }
}