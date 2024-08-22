using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DuplicateDetectorCore;

namespace DuplicateDetector
{
    public partial class DuplicateDetailsWindow : Window, INotifyPropertyChanged
    {
        public List<DuplicateFileInfo> Duplicates { get; set; }

        //public bool IsDiffToolEnabled
        //{
        //    get
        //    {
        //        if (ListViewDuplicateDetails is null || ListViewDuplicateDetails.SelectedItems is null)
        //            return false;

        //        return ListViewDuplicateDetails.SelectedItems.Count == 2;
        //    }
        //    set
        //    {
        //        OnPropertyChanged();
        //    }
        //}

        public DuplicateDetailsWindow(string hash, List<DuplicateFileInfo> duplicates)
        {
            InitializeComponent();

            Title = "Possibly Duplicate Files With Hash " + hash;

            Duplicates = duplicates;

            ListViewDuplicateDetails.ItemsSource = Duplicates;

            SetInitialSort();
        }

        private void MenuItemDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (ListViewDuplicateDetails.SelectedItems.Count > 0)
            {
                var filesToDelete = new List<DuplicateFileInfo>();

                foreach (DuplicateFileInfo fileInfo in ListViewDuplicateDetails.SelectedItems)
                {
                    filesToDelete.Add(fileInfo);
                }

                DeleteFiles(filesToDelete);
            }
        }

        private void MenuItemOpenWithTextEditorClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateFileInfo fileInfo)
            {
                OpenFileInEditor(fileInfo);
            }
        }

        private void ListViewDuplicateDetails_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //IsDiffToolEnabled = ListViewDuplicateDetails.SelectedItems.Count == 2;
        }

        private void ButtonDiffClicked(object sender, RoutedEventArgs e)
        {
            if (ListViewDuplicateDetails.SelectedItems.Count != 2)
                return;

            if (ListViewDuplicateDetails.SelectedItems[0] is DuplicateFileInfo fileInfo1 &&
                ListViewDuplicateDetails.SelectedItems[1] is DuplicateFileInfo fileInfo2)
            {
                CompareFilesInDiffTool(fileInfo1, fileInfo2);
            }
        }

        private void ShowFileInExplorer(string path)
        {
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        private void ShowFileInExplorer(DuplicateFileInfo fileInfo)
        {
            ShowFileInExplorer(Path.Combine(fileInfo.Path, fileInfo.FileName));
        }

        private void OpenFileInEditor(string path)
        {
            bool NotepadPlusPlusFound = false;

            if (Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Notepad++") is RegistryKey keyNotepadPlusPlus)
            {
                if (keyNotepadPlusPlus.GetValue(null) is string str)
                {
                    var notepadPlusPlusPath = Path.Combine(str, @"notepad++.exe");

                    if (File.Exists(notepadPlusPlusPath))
                    {
                        NotepadPlusPlusFound = true;

                        var processStartInfo = new System.Diagnostics.ProcessStartInfo();

                        processStartInfo.FileName = notepadPlusPlusPath;
                        processStartInfo.ArgumentList.Add(path);

                        System.Diagnostics.Process.Start(processStartInfo);
                    }
                }
            }

            if (!NotepadPlusPlusFound)
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo();

                processStartInfo.FileName = "notepad.exe";
                processStartInfo.ArgumentList.Add(path);

                System.Diagnostics.Process.Start(processStartInfo);
            }
        }

        private void OpenFileInEditor(DuplicateFileInfo fileInfo)
        {
            OpenFileInEditor(Path.Combine(fileInfo.Path, fileInfo.FileName));
        }

        private void OpenFileInHexEditor(DuplicateFileInfo fileInfo)
        {
            if (File.Exists(@"Tools\HxD\HxD64.exe"))
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo();

                startInfo.FileName = @"Tools\HxD\HxD64.exe";
                startInfo.ArgumentList.Add(Path.Combine(fileInfo.Path, fileInfo.FileName));

                System.Diagnostics.Process.Start(startInfo);
            }
        }

        private void CompareFilesInDiffTool(DuplicateFileInfo info1, DuplicateFileInfo info2)
        {
            var path1 = Path.Combine(info1.Path, info1.FileName);
            var path2 = Path.Combine(info2.Path, info2.FileName);

            if (Path.GetExtension(path1).Trim().ToLower() == ".pdf" &&
                Path.GetExtension(path2).Trim().ToLower() == ".pdf" &&
                File.Exists(@"Tools\DiffPDF\DiffpdfPortable.exe"))
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.FileName = @"Tools\DiffPDF\DiffpdfPortable.exe";

                startInfo.ArgumentList.Add(path1);
                startInfo.ArgumentList.Add(path2);

                System.Diagnostics.Process.Start(startInfo);
            }
            else
            {
                bool winMergeFound = false;
                string winMergePath = null;

                if (File.Exists(@"Tools\WinMerge\WinMergeU.exe"))
                {
                    winMergeFound = true;
                    winMergePath = @"Tools\WinMerge\WinMergeU.exe";
                }
                else
                {
                    var winMergeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Thingamahoochie\WinMerge");

                    if (winMergeKey != null)
                    {
                        if (winMergeKey.GetValue("Executable") is string path && File.Exists(path))
                        {
                            winMergeFound = true;
                            winMergePath = path;
                        }
                    }
                }

                if (winMergeFound)
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo();

                    startInfo.FileName = winMergePath;

                    startInfo.ArgumentList.Add(path1);
                    startInfo.ArgumentList.Add(path2);

                    System.Diagnostics.Process.Start(startInfo);
                }

                // WinMerge not found
                else
                {
                    if (MessageBox.Show(this, "WinMerge not found.\n\nWinMerge is a free and open source tool to compare files. Would you like to visit its download page now?", null, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        var url = "https://winmerge.org/downloads/";

                        var startInfo = new System.Diagnostics.ProcessStartInfo();

                        startInfo.FileName = url;
                        startInfo.UseShellExecute = true;

                        System.Diagnostics.Process.Start(startInfo);
                    }
                }
            }
        }

        private void MenuItemShowInFolderClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateFileInfo fileInfo)
            {
                ShowFileInExplorer(fileInfo);
            }
        }

        #region Sorting
        GridViewColumnHeader _lastHeaderClicked = null;
        ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            if (sortBy == "Value.TotalSizeReadable")
            {
                sortBy = "Value.TotalSize";
            }

            ICollectionView dataView =
              CollectionViewSource.GetDefaultView(ListViewDuplicateDetails.ItemsSource);

            dataView.SortDescriptions.Clear();
            SortDescription sd = new SortDescription(sortBy, direction);
            dataView.SortDescriptions.Add(sd);
            dataView.Refresh();
        }

        private void SetInitialSort()
        {
            Sort("FileName", ListSortDirection.Ascending);

            _lastHeaderClicked = ColumnHeaderFileName;
            _lastDirection = ListSortDirection.Ascending;
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
                        direction = ListSortDirection.Ascending;
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

        private void ListViewItemDoubleClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.Source is ListViewItem lvi)
            {
                if (lvi.Content is DuplicateFileInfo info)
                {
                    ViewFile(info);
                }
            }
        }

        private void MenuItemOpenWithHexEditorClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateFileInfo fileInfo)
            {
                OpenFileInHexEditor(fileInfo);
            }
        }

        private void ViewFile(DuplicateFileInfo fileInfo)
        {
            var fullPath = Path.Combine(fileInfo.Path, fileInfo.FileName);

            if (!File.Exists(fullPath))
                return;

            var startInfo = new System.Diagnostics.ProcessStartInfo();

            startInfo.FileName = fullPath;
            startInfo.UseShellExecute = true;

            System.Diagnostics.Process.Start(startInfo);
        }

        private void DeleteFiles(List<DuplicateFileInfo> files)
        {
            if (MessageBox.Show(this,
                        files.Count == 1 ? $"Delete {files[0].FileName}?" : $"Delete these {files.Count} files?",
                        "Confirm deletion of files",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Exclamation) != MessageBoxResult.OK)
            {
                return;
            }


            foreach (var file in files)
            {
                var fullPath = Path.Combine(file.Path, file.FileName);

                try
                {
                    File.Delete(fullPath);

                    Duplicates.Remove(file);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Could not delete {fullPath}:\n\n{ex.Message}", null, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // Update List View
            CollectionViewSource.GetDefaultView(ListViewDuplicateDetails.ItemsSource).Refresh();
        }

        private void ListViewDuplicateDetails_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ListViewDuplicateDetails.SelectedItems.Count == 0)
                return;

            // Create list of selected file info
            var fileInfos = new List<DuplicateFileInfo>();

            foreach (DuplicateFileInfo info in ListViewDuplicateDetails.SelectedItems)
            {
                fileInfos.Add(info);
            }

            // Action depending on which key was pressed
            switch (e.Key)
            {
                // Open File
                case System.Windows.Input.Key.Enter:

                    foreach (var item in fileInfos)
                    {
                        ViewFile(item);
                    }

                    break;


                // Delete file(s)
                case System.Windows.Input.Key.Delete:

                    DeleteFiles(fileInfos);

                    break;

                // Show in folder
                case System.Windows.Input.Key.F:

                    foreach (var item in fileInfos)
                    {
                        ShowFileInExplorer(item);
                    }

                    break;

                // Open in Text Editor
                case System.Windows.Input.Key.T:

                    foreach (var item in fileInfos)
                    {
                        OpenFileInEditor(item);
                    }

                    break;

                // Open in Hex Editor
                case System.Windows.Input.Key.X:

                    foreach (var item in fileInfos)
                    {
                        OpenFileInHexEditor(item);
                    }

                    break;
            }
        }

        private void MenuItemCompareFilesClicked(object sender, RoutedEventArgs e)
        {
            if (ListViewDuplicateDetails.SelectedItems.Count != 2)
                return;

            if (ListViewDuplicateDetails.SelectedItems[0] is DuplicateFileInfo info1 &&
                ListViewDuplicateDetails.SelectedItems[1] is DuplicateFileInfo info2)
            {
                CompareFilesInDiffTool(info1, info2);
            }
        }

        private void MenuItemOpenFileClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DuplicateFileInfo fileInfo)
            {
                ViewFile(fileInfo);
            }
        }
    }
}
