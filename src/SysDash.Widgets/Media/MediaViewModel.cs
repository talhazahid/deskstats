using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Mvvm;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Media;

/// <summary>
/// Now-playing widget powered by Windows.Media.Control (SMTC). Windows desktop
/// apps are allowed to read the global media session without registering a
/// background task, so this is a best-effort read: any failure shows a friendly
/// "nothing playing" state instead of an error.
/// </summary>
public sealed class MediaViewModel : WidgetViewModel
{
    private readonly DispatcherTimer _timer;
    private bool _refreshing;
    private string _titleText = "Nothing playing";
    private string _artistText = string.Empty;
    private string _appNameText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isPlaying;
    private bool _hasSession;
    private ImageSource? _thumbnail;

    public MediaViewModel(IThreadMarshaller marshaller, MediaSettings settings)
        : base(marshaller, settings)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public MediaSettings Options => (MediaSettings)Settings;

    public override string Title => "Now Playing";

    public string TitleText
    {
        get => _titleText;
        private set => SetProperty(ref _titleText, value);
    }

    public string ArtistText
    {
        get => _artistText;
        private set => SetProperty(ref _artistText, value);
    }

    public string AppNameText
    {
        get => _appNameText;
        private set => SetProperty(ref _appNameText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public bool HasSession
    {
        get => _hasSession;
        private set => SetProperty(ref _hasSession, value);
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    protected override void OnActivated()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    protected override void OnDispose()
    {
        _timer.Stop();
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        // Media state is read directly from SMTC; no hardware snapshot needed.
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "SMTC is best-effort and must never break the shell.")]
    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();

            if (session is null)
            {
                HasSession = false;
                IsPlaying = false;
                TitleText = "Nothing playing";
                ArtistText = string.Empty;
                AppNameText = string.Empty;
                StatusText = string.Empty;
                Thumbnail = null;
                return;
            }

            HasSession = true;
            AppNameText = Options.ShowAppName ? session.SourceAppUserModelId ?? string.Empty : string.Empty;

            var playback = session.GetPlaybackInfo();
            var status = playback?.PlaybackStatus;
            IsPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            StatusText = status switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "Playing",
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "Paused",
                _ => string.Empty,
            };

            GlobalSystemMediaTransportControlsSessionMediaProperties props =
                await session.TryGetMediaPropertiesAsync();

            if (props is not null)
            {
                TitleText = string.IsNullOrWhiteSpace(props.Title) ? "Untitled" : props.Title;
                ArtistText = props.Artist ?? string.Empty;

                if (Options.ShowThumbnail && props.Thumbnail is not null)
                {
                    Thumbnail = await LoadThumbnailAsync(props.Thumbnail);
                }
                else
                {
                    Thumbnail = null;
                }
            }
        }
        catch (Exception)
        {
            // SMTC unavailable (policy/OS version) — show the idle state.
            if (HasSession)
            {
                HasSession = false;
                TitleText = "Nothing playing";
                ArtistText = string.Empty;
                StatusText = string.Empty;
                Thumbnail = null;
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
            stream.Seek(0);
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);

            byte[] bytes = new byte[(int)stream.Size];
            reader.ReadBytes(bytes);

            using var memory = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}