using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DRAnalyzer.App.Models;

public sealed class AudioFileItem : INotifyPropertyChanged
{
    private string _artist = "";
    private string _album = "";
    private string _track = "";
    private string _title = "";
    private string _taggedDR = "";
    private string _dr = "";
    private string _albumDR = "";
    private string _calculatedAlbumDR = "";
    private string _albumArtist = "";
    private string _peak = "";
    private string _rms = "";
    private string _status = "";
    private string _filePath = "";
    private bool _hasOwnedTrackDrTag;
    private bool _hasOwnedAlbumDrTag;

    public string Artist
    {
        get => _artist;
        set => SetField(ref _artist, value);
    }

    public string Album
    {
        get => _album;
        set => SetField(ref _album, value);
    }

    public string Track
    {
        get => _track;
        set => SetField(ref _track, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string TaggedDR
    {
        get => _taggedDR;
        set => SetField(ref _taggedDR, value);
    }

    public string DR
    {
        get => _dr;
        set => SetField(ref _dr, value);
    }

    public string AlbumDR
    {
        get => _albumDR;
        set => SetField(ref _albumDR, value);
    }

    public string CalculatedAlbumDR
    {
        get => _calculatedAlbumDR;
        set => SetField(ref _calculatedAlbumDR, value);
    }

    public string AlbumArtist
    {
        get => _albumArtist;
        set => SetField(ref _albumArtist, value);
    }

    public string Peak
    {
        get => _peak;
        set => SetField(ref _peak, value);
    }

    public string RMS
    {
        get => _rms;
        set => SetField(ref _rms, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => SetField(ref _filePath, value);
    }


    public bool HasOwnedTrackDrTag
    {
        get => _hasOwnedTrackDrTag;
        set => SetField(ref _hasOwnedTrackDrTag, value);
    }

    public bool HasOwnedAlbumDrTag
    {
        get => _hasOwnedAlbumDrTag;
        set => SetField(ref _hasOwnedAlbumDrTag, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(
        ref bool field,
        bool value,
        [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;

        field = value;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private void SetField(
        ref string field,
        string value,
        [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;

        field = value;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
