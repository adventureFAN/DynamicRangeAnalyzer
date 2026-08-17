using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using DRAnalyzer.App.Models;
using DRAnalyzer.App.Settings;
using DRAnalyzer.Core.Analysis;
using DRAnalyzer.Core.Metadata;
using DRAnalyzer.Core.Tagging;

namespace DRAnalyzer.App;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".flac",
            ".wav",
            ".mp3",
            ".ogg",
            ".opus",
            ".m4a",
            ".aac",
            ".aiff",
            ".aif",
            ".wma",
            ".ape",
            ".wv"
        };

    private sealed record TableColumnDefault(
        string Key,
        int DisplayIndex,
        DataGridLength Width);

    private static readonly TableColumnDefault[] DefaultTableLayout =
    {
        new("Artist", 0, new DataGridLength(0.95, DataGridLengthUnitType.Star)),
        new("Album", 1, new DataGridLength(0.95, DataGridLengthUnitType.Star)),
        new("TrackNumber", 2, new DataGridLength(40)),
        new("Title", 3, new DataGridLength(2.15, DataGridLengthUnitType.Star)),
        new("TrackTagDr", 4, new DataGridLength(68)),
        new("TrackDr", 5, new DataGridLength(58)),
        new("AlbumTagDr", 6, new DataGridLength(76)),
        new("AlbumDr", 7, new DataGridLength(68)),
        new("Peak", 8, new DataGridLength(80)),
        new("Rms", 9, new DataGridLength(80)),
        new("Status", 10, new DataGridLength(90))
    };

    private enum BusyOperation
    {
        None,
        Loading,
        Analysis,
        TagWriting,
        TagRemoval
    }

    public ObservableCollection<AudioFileItem> AudioFiles { get; } = new();

    private bool _isBusy;
    private bool _cancelRequested;
    private BusyOperation _activeOperation = BusyOperation.None;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        ApplySavedFont();
        ApplySavedTableLayout();
        UpdateThemeMenuChecks();
        RefreshUiState();
    }

    private void ExitMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new AboutWindow
            {
                Owner = this,
                FontFamily = FontFamily,
                FontSize = FontSize
            };

        dialog.ShowDialog();
    }

    private void ChangeFontMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new FontSettingsWindow(
                FontFamily,
                FontSize)
            {
                Owner = this
            };

        if (dialog.ShowDialog() != true)
            return;

        ApplyFont(
            new FontFamily(
                dialog.SelectedFontFamilyName),
            dialog.SelectedFontSizeDip);

        AppFontSettings.Save(
            dialog.SelectedFontFamilyName,
            dialog.SelectedFontSizeDip);
    }

    private void ResetFontMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        AppFontSettings.Reset();

        ApplyFont(
            SystemFonts.MessageFontFamily,
            SystemFonts.MessageFontSize);
    }

    private void ApplySavedFont()
    {
        var preference =
            AppFontSettings.Load();

        if (preference is null)
        {
            ApplyFont(
                SystemFonts.MessageFontFamily,
                SystemFonts.MessageFontSize);

            return;
        }

        var fontFamily =
            Fonts.SystemFontFamilies
                .FirstOrDefault(
                    family =>
                        string.Equals(
                            family.Source,
                            preference.FontFamilyName,
                            StringComparison.OrdinalIgnoreCase));

        if (fontFamily is null)
        {
            ApplyFont(
                SystemFonts.MessageFontFamily,
                SystemFonts.MessageFontSize);

            return;
        }

        ApplyFont(
            fontFamily,
            preference.FontSizeDip);
    }

    private void ApplyFont(
        FontFamily fontFamily,
        double fontSizeDip)
    {
        FontFamily =
            fontFamily;

        FontSize =
            fontSizeDip;
    }

    private IReadOnlyDictionary<string, DataGridColumn> GetTableColumns()
    {
        return new Dictionary<string, DataGridColumn>(
            StringComparer.Ordinal)
        {
            ["Artist"] = ArtistColumn,
            ["Album"] = AlbumColumn,
            ["TrackNumber"] = TrackNumberColumn,
            ["Title"] = TitleColumn,
            ["TrackTagDr"] = TrackTagDrColumn,
            ["TrackDr"] = TrackDrColumn,
            ["AlbumTagDr"] = AlbumTagDrColumn,
            ["AlbumDr"] = AlbumDrColumn,
            ["Peak"] = PeakColumn,
            ["Rms"] = RmsColumn,
            ["Status"] = StatusColumn
        };
    }

    private void ApplySavedTableLayout()
    {
        var saved =
            AppTableLayoutSettings.Load();

        if (saved is null)
        {
            ApplyDefaultTableLayout();
            return;
        }

        var columns =
            GetTableColumns();

        var validEntries =
            saved.Columns
                .Where(entry =>
                    columns.ContainsKey(entry.Key) &&
                    entry.DisplayIndex >= 0 &&
                    entry.DisplayIndex < columns.Count)
                .GroupBy(
                    entry => entry.Key,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

        if (validEntries
                .Select(entry => entry.DisplayIndex)
                .Distinct()
                .Count() != validEntries.Length)
        {
            ApplyDefaultTableLayout();
            return;
        }

        var orderedKeys =
            validEntries
                .OrderBy(entry => entry.DisplayIndex)
                .Select(entry => entry.Key)
                .Concat(
                    DefaultTableLayout
                        .Select(item => item.Key)
                        .Where(key =>
                            validEntries.All(
                                entry => entry.Key != key)))
                .ToArray();

        for (var index = 0; index < orderedKeys.Length; index++)
        {
            columns[orderedKeys[index]].DisplayIndex = index;
        }

        foreach (var entry in validEntries)
        {
            if (TryCreateSavedWidth(
                    entry,
                    out var width))
            {
                columns[entry.Key].Width = width;
            }
        }
    }

    private void ApplyDefaultTableLayout()
    {
        var columns =
            GetTableColumns();

        foreach (var item in DefaultTableLayout)
        {
            columns[item.Key].DisplayIndex =
                item.DisplayIndex;

            columns[item.Key].Width =
                item.Width;
        }
    }

    private void SaveTableLayout()
    {
        var columns =
            GetTableColumns();

        var entries =
            columns
                .Select(pair =>
                {
                    var width = pair.Value.Width;

                    return new AppTableColumnLayout(
                        pair.Key,
                        pair.Value.DisplayIndex,
                        width.Value,
                        width.UnitType.ToString());
                })
                .OrderBy(entry => entry.DisplayIndex)
                .ToList();

        AppTableLayoutSettings.Save(
            new AppTableLayoutPreference(entries));
    }

    private static bool TryCreateSavedWidth(
        AppTableColumnLayout entry,
        out DataGridLength width)
    {
        width = default;

        if (!double.IsFinite(entry.WidthValue) ||
            entry.WidthValue <= 0)
        {
            return false;
        }

        if (!Enum.TryParse<DataGridLengthUnitType>(
                entry.WidthUnit,
                ignoreCase: true,
                out var unitType))
        {
            return false;
        }

        switch (unitType)
        {
            case DataGridLengthUnitType.Pixel:
                if (entry.WidthValue < 32 ||
                    entry.WidthValue > 2000)
                {
                    return false;
                }

                width =
                    new DataGridLength(
                        entry.WidthValue,
                        DataGridLengthUnitType.Pixel);

                return true;

            case DataGridLengthUnitType.Star:
                if (entry.WidthValue < 0.1 ||
                    entry.WidthValue > 50)
                {
                    return false;
                }

                width =
                    new DataGridLength(
                        entry.WidthValue,
                        DataGridLengthUnitType.Star);

                return true;

            default:
                return false;
        }
    }

    private void ResetTableLayoutMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        AppTableLayoutSettings.Reset();
        ApplyDefaultTableLayout();
        SaveTableLayout();
    }

    private void ThemeSystemMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetThemePreference(
            AppThemePreference.System);
    }

    private void ThemeLightMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetThemePreference(
            AppThemePreference.Light);
    }

    private void ThemeDarkMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetThemePreference(
            AppThemePreference.Dark);
    }

    private void SetThemePreference(
        AppThemePreference preference)
    {
        if (Application.Current is not App app)
            return;

        app.SetThemePreference(
            preference);

        UpdateThemeMenuChecks();
    }

    private void UpdateThemeMenuChecks()
    {
        var preference =
            Application.Current is App app
                ? app.ThemePreference
                : AppThemePreference.System;

        ThemeSystemMenuItem.IsChecked =
            preference ==
            AppThemePreference.System;

        ThemeLightMenuItem.IsChecked =
            preference ==
            AppThemePreference.Light;

        ThemeDarkMenuItem.IsChecked =
            preference ==
            AppThemePreference.Dark;
    }

    private void Window_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        var modifiers =
            Keyboard.Modifiers;

        if (modifiers == ModifierKeys.Control &&
            e.Key == Key.O)
        {
            if (AddFilesButton.IsEnabled)
            {
                AddFiles_Click(
                    AddFilesButton,
                    new RoutedEventArgs());
            }

            e.Handled = true;
            return;
        }

        if (modifiers ==
                (ModifierKeys.Control |
                 ModifierKeys.Shift) &&
            e.Key == Key.O)
        {
            if (AddFolderButton.IsEnabled)
            {
                AddFolder_Click(
                    AddFolderButton,
                    new RoutedEventArgs());
            }

            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control &&
            e.Key == Key.L)
        {
            if (ClearListButton.IsEnabled)
            {
                ClearListButton_Click(
                    ClearListButton,
                    new RoutedEventArgs());
            }

            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None &&
            e.Key == Key.Delete)
        {
            if (RemoveSelectedMenuItem.IsEnabled)
            {
                RemoveSelectedMenuItem_Click(
                    RemoveSelectedMenuItem,
                    new RoutedEventArgs());
            }

            e.Handled = true;
            return;
        }

    }
    private void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (!_isBusy)
        {
            SaveTableLayout();
            return;
        }

        e.Cancel = true;

        var message =
            _activeOperation == BusyOperation.Loading
                ? "Files are still being loaded. Please wait for loading to finish before closing Dynamic Range Analyzer."
                : "An operation is still running. " +
                  "Use Cancel and wait for the current file to finish " +
                  "before closing Dynamic Range Analyzer.";

        MessageBox.Show(
            message,
            "Operation in progress",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RequestCancellation()
    {
        if (!_isBusy ||
            _cancelRequested)
        {
            return;
        }

        _cancelRequested = true;

        RefreshUiState();

        StatusText.Text =
            "Cancellation requested — finishing current file...";
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select audio files",
            Multiselect = true,
            Filter =
                "Audio files|*.flac;*.wav;*.mp3;*.ogg;*.opus;*.m4a;*.aac;*.aiff;*.aif;*.wma;*.ape;*.wv|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog() == true)
            await AddFilesAsync(dialog.FileNames);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Select music folder",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            await AddFolderAsync(dialog.FolderName);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects =
            !_isBusy &&
            e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isBusy)
            return;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        foreach (var path in paths)
        {
            if (File.Exists(path))
                await AddFilesAsync(new[] { path });
            else if (Directory.Exists(path))
                await AddFolderAsync(path);
        }
    }

    private async Task AddFolderAsync(string folderPath)
    {
        if (_isBusy)
            return;

        SetBusy(
            true,
            BusyOperation.Loading);

        ResetProgress();
        AnalysisProgress.IsIndeterminate = true;
        StatusText.Text = "Scanning folder...";

        try
        {
            var files =
                await Task.Run(
                    () =>
                        Directory
                            .EnumerateFiles(
                                folderPath,
                                "*.*",
                                new EnumerationOptions
                                {
                                    RecurseSubdirectories = true,
                                    IgnoreInaccessible = true
                                })
                            .Where(IsSupportedAudioFile)
                            .ToArray());

            AnalysisProgress.IsIndeterminate = false;

            await AddFilesAsync(files);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "The folder could not be read completely.";

            MessageBox.Show(
                UserFacingErrorFormatter.ForFolderRead(ex),
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            AnalysisProgress.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private async Task AddFilesAsync(IEnumerable<string> filePaths)
    {
        if (_isBusy &&
            _activeOperation != BusyOperation.Loading)
        {
            return;
        }

        var ownsBusyState =
            !_isBusy;

        if (ownsBusyState)
        {
            SetBusy(
                true,
                BusyOperation.Loading);
        }

        try
        {
            var metadataErrors =
                new List<string>();

            var existingFiles =
                AudioFiles
                    .Select(file => file.FilePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var filesToLoad =
                filePaths
                    .Where(File.Exists)
                    .Where(IsSupportedAudioFile)
                    .Select(Path.GetFullPath)
                    .Where(existingFiles.Add)
                    .ToArray();

            AnalysisProgress.IsIndeterminate = false;
            AnalysisProgress.Minimum = 0;
            AnalysisProgress.Maximum =
                Math.Max(1, filesToLoad.Length);
            AnalysisProgress.Value = 0;

            if (filesToLoad.Length == 0)
            {
                StatusText.Text =
                    AudioFiles.Count == 0
                        ? "No supported audio files added."
                        : "No new supported audio files found.";

                return;
            }

            for (var index = 0;
                 index < filesToLoad.Length;
                 index++)
            {
                var fullPath =
                    filesToLoad[index];

                StatusText.Text =
                    $"Loading metadata {index + 1} of " +
                    $"{filesToLoad.Length} — " +
                    $"{Path.GetFileName(fullPath)}";

                try
                {
                    var metadata =
                        await Task.Run(
                            () =>
                                AudioMetadataReader.Read(fullPath));

                    AudioFiles.Add(
                        new AudioFileItem
                        {
                            Artist = metadata.Artist,
                            Album = metadata.Album,
                            AlbumArtist = metadata.AlbumArtist,
                            Track = metadata.Track,
                            Title = metadata.Title,

                            TaggedDR =
                                FormatDr(metadata.DynamicRange),

                            AlbumDR =
                                FormatDr(metadata.AlbumDynamicRange),

                            HasOwnedTrackDrTag =
                                metadata.HasOwnedDynamicRangeTag,

                            HasOwnedAlbumDrTag =
                                metadata.HasOwnedAlbumDynamicRangeTag,

                            FilePath = fullPath,
                            Status = "Ready"
                        });
                }
                catch (Exception ex)
                {
                    metadataErrors.Add(
                        $"{Path.GetFileName(fullPath)}\n" +
                        UserFacingErrorFormatter.ForMetadata(ex));

                    AudioFiles.Add(
                        new AudioFileItem
                        {
                            Title =
                                Path.GetFileNameWithoutExtension(fullPath),

                            FilePath = fullPath,
                            Status = "Metadata error"
                        });
                }

                AnalysisProgress.Value =
                    index + 1;

                UpdateCollectionStatus();
            }

            RefreshUiState();

            StatusText.Text =
                string.Empty;

            if (metadataErrors.Count > 0)
            {
                var details =
                    string.Join(
                        "\n\n",
                        metadataErrors.Take(5));

                if (metadataErrors.Count > 5)
                {
                    details +=
                        $"\n\n... and {metadataErrors.Count - 5} more.";
                }

                MessageBox.Show(
                    $"Metadata could not be read for {metadataErrors.Count} " +
                    (metadataErrors.Count == 1 ? "file." : "files.") +
                    $"\n\n{details}",
                    "Metadata Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (ownsBusyState)
            {
                SetBusy(false);
            }
        }
    }

    private void ClearListButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        AudioFiles.Clear();

        ResetProgress();

        StatusText.Text =
            string.Empty;

        RefreshUiState();
    }

    private void RemoveSelectedMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var selectedItems =
            AudioFilesGrid.SelectedItems
                .OfType<AudioFileItem>()
                .ToArray();

        if (selectedItems.Length == 0)
            return;

        foreach (var item in selectedItems)
        {
            AudioFiles.Remove(item);
        }

        CalculateAlbumDynamicRange();
        ResetProgress();
        RefreshUiState();

        StatusText.Text =
            string.Empty;
    }

    private void AudioFilesGrid_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshUiState();
    }
    private async void AnalyzeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
        {
            if (_activeOperation == BusyOperation.Analysis)
            {
                RequestCancellation();
            }

            return;
        }

        if (AudioFiles.Count == 0)
            return;

        var alreadyAnalyzed =
            AudioFiles.Count(IsAnalyzedInCurrentSession);

        if (alreadyAnalyzed == AudioFiles.Count)
        {
            AnalysisProgress.Minimum = 0;
            AnalysisProgress.Maximum = AudioFiles.Count;
            AnalysisProgress.Value = AudioFiles.Count;

            StatusText.Text =
                "All files are already analyzed in this session.";

            return;
        }

        _cancelRequested = false;
        SetBusy(
            true,
            BusyOperation.Analysis);

        AnalysisProgress.Minimum = 0;
        AnalysisProgress.Maximum = AudioFiles.Count;
        AnalysisProgress.Value = alreadyAnalyzed;

        var successfulThisRun = 0;
        var cancelled = false;
        var errors = new List<string>();

        try
        {
            for (var index = 0;
                 index < AudioFiles.Count;
                 index++)
            {
                if (_cancelRequested)
                {
                    cancelled = true;
                    break;
                }

                var item = AudioFiles[index];

                if (IsAnalyzedInCurrentSession(item))
                    continue;

                item.Status =
                    "Analyzing...";

                StatusText.Text =
                    $"Analyzing {index + 1} of {AudioFiles.Count} — " +
                    $"{Path.GetFileName(item.FilePath)}";

                try
                {
                    var result =
                        await Task.Run(
                            () =>
                                DynamicRangeAnalyzer.Analyze(
                                    item.FilePath));

                    item.DR =
                        $"DR{result.RoundedDynamicRange}";

                    item.Peak =
                        FormatDb(result.PeakDb);

                    item.RMS =
                        FormatDb(result.RmsDb);

                    if (string.IsNullOrWhiteSpace(item.TaggedDR))
                    {
                        item.Status =
                            "Done";
                    }
                    else if (string.Equals(
                                 item.TaggedDR,
                                 item.DR,
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status =
                            "Done ✓";
                    }
                    else
                    {
                        item.Status =
                            $"{item.TaggedDR} ≠ {item.DR}";
                    }

                    successfulThisRun++;
                }
                catch (Exception ex)
                {
                    item.Status =
                        "Error";

                    errors.Add(
                        $"{item.Title}\n" +
                        UserFacingErrorFormatter.ForAnalysis(ex));
                }

                var analyzedTotal =
                    AudioFiles.Count(IsAnalyzedInCurrentSession);

                AnalysisProgress.Value =
                    analyzedTotal;

                UpdateCollectionStatus();

                if (_cancelRequested &&
                    analyzedTotal < AudioFiles.Count)
                {
                    cancelled = true;
                    break;
                }
            }

            CalculateAlbumDynamicRange();

        }
        finally
        {
            SetBusy(false);
        }

        var totalAnalyzed =
            AudioFiles.Count(IsAnalyzedInCurrentSession);

        StatusText.Text =
            cancelled
                ? $"Analysis cancelled. {totalAnalyzed} of " +
                  $"{AudioFiles.Count} files analyzed."
                : errors.Count == 0
                    ? string.Empty
                    : $"{successfulThisRun} succeeded this run, " +
                      $"{errors.Count} failed. {totalAnalyzed} of " +
                      $"{AudioFiles.Count} analyzed.";

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join(
                    "\n\n",
                    errors.Take(5)),
                "Analysis Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool IsAnalyzedInCurrentSession(
        AudioFileItem item)
    {
        return
            TryParseDr(
                item.DR,
                out _);
    }

    private async void WriteTagsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
        {
            if (_activeOperation == BusyOperation.TagWriting)
            {
                RequestCancellation();
            }

            return;
        }

        var writableItems =
            AudioFiles
                .Where(IsWritableAnalyzedFile)
                .ToArray();

        var isUpdate =
            writableItems.Length > 0 &&
            writableItems.All(
                HasOwnedDrTags);

        var writeActionText =
            isUpdate
                ? "Update DR Tags"
                : "Write DR Tags";

        if (writableItems.Length == 0)
        {
            MessageBox.Show(
                "There are no analyzed files " +
                "that can receive DR tags.",
                writeActionText,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var skippedCount =
            AudioFiles.Count -
            writableItems.Length;

        var message =
            (isUpdate
                ? $"DR tags will be updated in {writableItems.Length} "
                : $"DR tags will be written to {writableItems.Length} ") +
            (writableItems.Length == 1
                ? "file"
                : "files") +
            ".\n\n" +
            "The files will be modified directly. " +
            "Dynamic Range Analyzer will not change other existing metadata " +
            "or embedded artwork.\n\n" +
            "Album DR is written only when a calculated Album DR " +
            "is shown for the track in the table.";

        if (skippedCount > 0)
        {
            message +=
                $"\n\n{skippedCount} unsupported " +
                (skippedCount == 1
                    ? "file will"
                    : "files will") +
                " be skipped.";
        }

        message +=
            "\n\nImportant: The calculated Album DR is based on " +
            "the tracks that are currently loaded and analyzed.\n\n" +
            (isUpdate
                ? "Update the tags now?"
                : "Write the tags now?");

        var answer =
            MessageBox.Show(
                message,
                writeActionText,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _cancelRequested = false;
        SetBusy(
            true,
            BusyOperation.TagWriting);

        AnalysisProgress.Minimum = 0;
        AnalysisProgress.Maximum = writableItems.Length;
        AnalysisProgress.Value = 0;

        var successful = 0;
        var processed = 0;
        var cancelled = false;
        var errors = new List<string>();

        try
        {
            for (var index = 0;
                 index < writableItems.Length;
                 index++)
            {
                if (_cancelRequested)
                {
                    cancelled = true;
                    break;
                }

                var item =
                    writableItems[index];

                StatusText.Text =
                    $"Writing DR tags {index + 1} of " +
                    $"{writableItems.Length} — " +
                    $"{Path.GetFileName(item.FilePath)}";

                item.Status =
                    "Writing tags...";

                try
                {
                    if (!TryParseDr(
                            item.DR,
                            out var trackDr))
                    {
                        throw new InvalidOperationException(
                            "The calculated track DR is invalid.");
                    }

                    int? albumDr = null;

                    if (TryParseDr(
                            item.CalculatedAlbumDR,
                            out var parsedAlbumDr))
                    {
                        albumDr =
                            parsedAlbumDr;
                    }

                    await Task.Run(
                        () =>
                            DynamicRangeTagWriter.Write(
                                item.FilePath,
                                trackDr,
                                albumDr));

                    item.TaggedDR =
                        $"DR{trackDr}";

                    item.HasOwnedTrackDrTag =
                        true;

                    if (albumDr.HasValue)
                    {
                        item.AlbumDR =
                            $"DR{albumDr.Value}";

                        item.HasOwnedAlbumDrTag =
                            true;
                    }

                    item.Status =
                        "Tags written ✓";

                    successful++;
                }
                catch (Exception ex)
                {
                    item.Status =
                        "Write error";

                    errors.Add(
                        $"{item.Title}\n" +
                        UserFacingErrorFormatter.ForTagging(ex));
                }

                processed++;

                AnalysisProgress.Value =
                    processed;

                UpdateCollectionStatus();

                if (_cancelRequested &&
                    processed < writableItems.Length)
                {
                    cancelled = true;
                    break;
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        StatusText.Text =
            cancelled
                ? $"DR tag writing cancelled after {processed} of " +
                  $"{writableItems.Length} files."
                : errors.Count == 0
                    ? string.Empty
                    : $"{successful} " +
                      (isUpdate ? "updated" : "written") +
                      $", {errors.Count} failed.";

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join(
                    "\n\n",
                    errors.Take(5)),
                "DR Tag Write Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void RemoveTagsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
        {
            if (_activeOperation == BusyOperation.TagRemoval)
            {
                RequestCancellation();
            }

            return;
        }

        var removableItems =
            AudioFiles
                .Where(IsRemovableTaggedFile)
                .ToArray();

        if (removableItems.Length == 0)
        {
            MessageBox.Show(
                "There are no loaded supported files " +
                "with DR tags to remove.",
                "Remove DR Tags",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var supportedWithoutTagsCount =
            AudioFiles.Count(
                item =>
                    DynamicRangeTagWriter.CanRemove(
                        item.FilePath) &&
                    !HasOwnedDrTags(item));

        var unsupportedCount =
            AudioFiles.Count(
                item =>
                    !DynamicRangeTagWriter.CanRemove(
                        item.FilePath));

        var message =
            $"DR tags will be removed from {removableItems.Length} " +
            (removableItems.Length == 1
                ? "file"
                : "files") +
            ".\n\n" +
            "Only DYNAMIC RANGE and ALBUM DYNAMIC RANGE will be removed. " +
            "Dynamic Range Analyzer will not change other metadata or embedded artwork.";

        if (supportedWithoutTagsCount > 0)
        {
            message +=
                $"\n\n{supportedWithoutTagsCount} supported " +
                (supportedWithoutTagsCount == 1
                    ? "file has"
                    : "files have") +
                " no DR tags and will be skipped.";
        }

        if (unsupportedCount > 0)
        {
            message +=
                $"\n\n{unsupportedCount} unsupported " +
                (unsupportedCount == 1
                    ? "file will"
                    : "files will") +
                " be skipped.";
        }

        message +=
            "\n\nRemove the DR tags now?";

        var answer =
            MessageBox.Show(
                message,
                "Remove DR Tags",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _cancelRequested = false;
        SetBusy(
            true,
            BusyOperation.TagRemoval);

        AnalysisProgress.Minimum = 0;
        AnalysisProgress.Maximum = removableItems.Length;
        AnalysisProgress.Value = 0;

        var successful = 0;
        var processed = 0;
        var cancelled = false;
        var errors = new List<string>();

        try
        {
            for (var index = 0;
                 index < removableItems.Length;
                 index++)
            {
                if (_cancelRequested)
                {
                    cancelled = true;
                    break;
                }

                var item =
                    removableItems[index];

                StatusText.Text =
                    $"Removing DR tags {index + 1} of " +
                    $"{removableItems.Length} — " +
                    $"{Path.GetFileName(item.FilePath)}";

                item.Status =
                    "Removing DR tags...";

                try
                {
                    await Task.Run(
                        () =>
                            DynamicRangeTagWriter.Remove(
                                item.FilePath));

                    item.HasOwnedTrackDrTag =
                        false;

                    item.HasOwnedAlbumDrTag =
                        false;

                    try
                    {
                        var refreshedMetadata =
                            await Task.Run(
                                () =>
                                    AudioMetadataReader.Read(
                                        item.FilePath));

                        item.TaggedDR =
                            FormatDr(refreshedMetadata.DynamicRange);

                        item.AlbumDR =
                            FormatDr(refreshedMetadata.AlbumDynamicRange);
                    }
                    catch
                    {
                        // Tag removal itself succeeded. A metadata refresh
                        // failure must not turn that into a write failure.
                        item.TaggedDR = "";
                        item.AlbumDR = "";
                    }

                    item.Status =
                        "DR tags removed ✓";

                    successful++;
                }
                catch (Exception ex)
                {
                    item.Status =
                        "Remove error";

                    errors.Add(
                        $"{item.Title}\n" +
                        UserFacingErrorFormatter.ForTagging(ex));
                }

                processed++;

                AnalysisProgress.Value =
                    processed;

                UpdateCollectionStatus();

                if (_cancelRequested &&
                    processed < removableItems.Length)
                {
                    cancelled = true;
                    break;
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        StatusText.Text =
            cancelled
                ? $"DR tag removal cancelled after {processed} of " +
                  $"{removableItems.Length} files."
                : errors.Count == 0
                    ? string.Empty
                    : $"{successful} removed, {errors.Count} failed.";

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join(
                    "\n\n",
                    errors.Take(5)),
                "DR Tag Removal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetBusy(
        bool isBusy,
        BusyOperation operation = BusyOperation.None)
    {
        _isBusy =
            isBusy;

        _activeOperation =
            isBusy
                ? operation
                : BusyOperation.None;

        if (!isBusy)
        {
            _cancelRequested = false;
        }

        RefreshUiState();
    }

    private void RefreshUiState()
    {
        var hasFiles =
            AudioFiles.Count > 0;

        var allFilesAnalyzed =
            hasFiles &&
            AudioFiles.All(
                item =>
                    TryParseDr(
                        item.DR,
                        out _));

        var hasAnalysisErrors =
            AudioFiles.Any(
                item =>
                    string.Equals(
                        item.Status,
                        "Error",
                        StringComparison.OrdinalIgnoreCase));

        AddFilesButton.IsEnabled =
            !_isBusy;

        AddFolderButton.IsEnabled =
            !_isBusy;

        ClearListButton.IsEnabled =
            !_isBusy &&
            hasFiles;

        RemoveSelectedMenuItem.IsEnabled =
            !_isBusy &&
            AudioFilesGrid.SelectedItems.Count > 0;

        var analysisIsActive =
            _isBusy &&
            _activeOperation == BusyOperation.Analysis;

        var tagWritingIsActive =
            _isBusy &&
            _activeOperation == BusyOperation.TagWriting;

        var tagRemovalIsActive =
            _isBusy &&
            _activeOperation == BusyOperation.TagRemoval;

        var writableAnalyzedItems =
            AudioFiles
                .Where(IsWritableAnalyzedFile)
                .ToArray();

        var showUpdateTags =
            writableAnalyzedItems.Length > 0 &&
            writableAnalyzedItems.All(
                HasOwnedDrTags);

        var writeTagsText =
            showUpdateTags
                ? "Update DR Tags"
                : "Write DR Tags";

        AnalyzeButton.Content =
            analysisIsActive
                ? "Cancel"
                : "Analyze";

        AnalyzeButton.IsEnabled =
            analysisIsActive
                ? !_cancelRequested
                : !_isBusy &&
                  hasFiles;

        WriteTagsButton.Content =
            tagWritingIsActive
                ? "Cancel"
                : writeTagsText;

        WriteTagsButton.IsEnabled =
            tagWritingIsActive
                ? !_cancelRequested
                : !_isBusy &&
                  allFilesAnalyzed &&
                  !hasAnalysisErrors &&
                  AudioFiles.Any(
                      IsWritableAnalyzedFile);

        RemoveTagsButton.Content =
            tagRemovalIsActive
                ? "Cancel"
                : "Remove DR Tags";

        var removeTagsIsEnabled =
            tagRemovalIsActive
                ? !_cancelRequested
                : !_isBusy &&
                  AudioFiles.Any(
                      IsRemovableTaggedFile);

        RemoveTagsButton.IsEnabled =
            removeTagsIsEnabled;

        AllowDrop =
            !_isBusy;

        DropHint.Visibility =
            hasFiles
                ? Visibility.Collapsed
                : Visibility.Visible;

        UpdateCollectionStatus();
    }

    private void UpdateCollectionStatus()
    {
        var total =
            AudioFiles.Count;

        var analyzed =
            AudioFiles.Count(
                item =>
                    TryParseDr(
                        item.DR,
                        out _));

        var errors =
            AudioFiles.Count(
                item =>
                    item.Status.Contains(
                        "error",
                        StringComparison.OrdinalIgnoreCase));

        CollectionStatusText.Text =
            $"{total} " +
            (total == 1
                ? "file"
                : "files") +
            $" • {analyzed} analyzed • {errors} " +
            (errors == 1
                ? "error"
                : "errors");
    }

    private void ResetProgress()
    {
        AnalysisProgress.IsIndeterminate = false;
        AnalysisProgress.Minimum = 0;
        AnalysisProgress.Maximum = 100;
        AnalysisProgress.Value = 0;
    }

    private static bool IsWritableAnalyzedFile(
        AudioFileItem item)
    {
        return
            DynamicRangeTagWriter.CanWrite(
                item.FilePath) &&
            TryParseDr(
                item.DR,
                out _);
    }

    private static bool IsRemovableTaggedFile(
        AudioFileItem item)
    {
        return
            DynamicRangeTagWriter.CanRemove(
                item.FilePath) &&
            HasOwnedDrTags(item);
    }

    private static bool HasOwnedDrTags(
        AudioFileItem item)
    {
        return
            item.HasOwnedTrackDrTag ||
            item.HasOwnedAlbumDrTag;
    }
    private void CalculateAlbumDynamicRange()
    {
        var analyzedItems =
            AudioFiles
                .Where(item => TryParseDr(item.DR, out _))
                .ToArray();

        foreach (var item in analyzedItems)
        {
            item.CalculatedAlbumDR = "";
        }

        var albumGroups =
            analyzedItems.GroupBy(
                item =>
                    AlbumGroupingKey.Create(
                        item.AlbumArtist,
                        item.Artist,
                        item.Album,
                        item.FilePath));

        foreach (var group in albumGroups)
        {
            var drValues =
                group
                    .Select(item =>
                    {
                        TryParseDr(item.DR, out var dr);
                        return dr;
                    })
                    .ToArray();

            if (drValues.Length == 0)
                continue;

            var albumDr =
                AlbumDynamicRangeCalculator.Calculate(
                    drValues);

            foreach (var item in group)
            {
                item.CalculatedAlbumDR =
                    $"DR{albumDr}";
            }
        }
    }

    private static bool TryParseDr(
        string value,
        out int dr)
    {
        dr = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();

        if (value.StartsWith(
                "DR",
                StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return int.TryParse(value, out dr);
    }

    private static string FormatDr(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        value = value.Trim();

        return value.StartsWith(
                   "DR",
                   StringComparison.OrdinalIgnoreCase)
            ? value.ToUpperInvariant()
            : $"DR{value}";
    }

    private static string FormatDb(double value)
    {
        if (double.IsNegativeInfinity(value))
            return "-∞ dB";

        return $"{value:+0.00;-0.00;0.00} dB";
    }

    private static bool IsSupportedAudioFile(
        string filePath)
    {
        return SupportedExtensions.Contains(
            Path.GetExtension(filePath));
    }
}







