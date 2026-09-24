using System.Diagnostics;
using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.System;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage : Page
{
    private static readonly double[] Speeds = { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };

    private MediaPlayer? _player;
    private MediaPlaybackItem? _playbackItem;
    private MediaSource? _currentSource;
    private volatile bool _tearingDown;
    private readonly List<MovieSubtitle> _attachedSubtitles = new();
    private bool _mediaOpened;
    private int _loadGen;

    private PlayerLaunch? _launch;
    private MovieVideo? _video;
    private string _quality = "";
    private bool _isPinned;

    // Keeps the PC awake while video is playing (display + sleep).
    private Windows.System.Display.DisplayRequest? _displayRequest;
    private bool _displayActive;

    private void UpdateDisplayRequest(bool playing)
    {
        try
        {
            if (playing && !_displayActive)
            {
                _displayRequest ??= new Windows.System.Display.DisplayRequest();
                _displayRequest.RequestActive();
                _displayActive = true;
            }
            else if (!playing && _displayActive)
            {
                _displayRequest?.RequestRelease();
                _displayActive = false;
            }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }
    private bool _seeking;
    private bool _switching;
    private bool _endReached;
    private long _pendingSeekMs;
    private DateTime _lastTimeChanged = DateTime.MinValue;
    private int _wasMuted;

    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private DateTime _lastSiteSave = DateTime.MinValue;
    private bool _isFullscreen;
    private Windows.Graphics.RectInt32 _savedBounds;

    /// <summary>
    /// Host window set by PlayerWindow. When null, falls back to the main window
    /// (kept for safety, the page is now hosted in a separate window).
    /// </summary>
    public Window? HostWindow { get; set; }

    private Window? Host => HostWindow ?? App.MainWindow;

    public PlayerPage()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => HideControls();
        _saveTimer.Tick += (_, _) => SavePosition();
        Loaded += (_, _) =>
        {
            BuildSpeedMenu();
            // Re-apply after activation: SetTitleBar may not stick
            // when called pre-activation from OnNavigatedTo.
            ApplyWindowChrome();
            ReturnFocus();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not PlayerLaunch launch)
        {
            App.TryLog(new Exception("[Player] OnNavigatedTo: no PlayerLaunch parameter"));
            return;
        }

        _launch = launch;
        _quality = SettingsService.Instance.DefaultQuality;
        App.TryLog(new Exception($"[Player] OnNavigatedTo: id={launch.Details.Id} voice={launch.Voice.TranslatorId}"));

        TitleText.Text = launch.Details.Name;
        UpdateEpisodeText();
        ApplyLocalization();
        ApplyWindowChrome();

        _ = LoadVideoAsync(seekToSaved: true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_isFullscreen) ExitFullscreen();
        TeardownPlayback();
    }

    private void TeardownPlayback()
    {
        _tearingDown = true;
        UpdateDisplayRequest(false);
        ShowSystemCursor();
        ClearPlaybackMarker();
        SavePosition();
        _saveTimer.Stop();
        _hideTimer.Stop();

        try
        {
            // Detach player from UI element BEFORE disposing to avoid rendering crashes
            PlayerElement.SetMediaPlayer(null);

            if (_player != null)
            {
                UnsubscribePlayerEvents();
                _player.Source = null;
                _player.Dispose();
                _player = null;
            }

            _playbackItem = null;
            _currentSource = null;
            _attachedSubtitles.Clear();
        }
        catch
        {
            // ignore teardown errors
        }
    }

    /// <summary>
    /// Called by PlayerWindow when it closes: saves position and releases
    /// playback resources. Does not touch the window presenter (the window
    /// is already gone at that point).
    /// </summary>
    public void Shutdown()
    {
        try
        {
            TeardownPlayback();
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private void SetupPlayer()
    {
        if (_player != null) return;

        _tearingDown = false;
        _player = new MediaPlayer();
        PlayerElement.SetMediaPlayer(_player);
        var volume = Math.Clamp(SettingsService.Instance.Volume, 0, 100);
        _player.Volume = volume / 100.0;
        VolumeSlider.Value = volume;
        UpdateMuteIcon();
        SubscribePlayerEvents();
        ApplySpeed();
        _saveTimer.Start();
        App.TryLog(new Exception("[Player] Native backend ready"));
    }

    private void SubscribePlayerEvents()
    {
        if (_player == null) return;
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.PlaybackSession.PositionChanged += OnPositionChanged;
        _player.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
    }

    private void UnsubscribePlayerEvents()
    {
        if (_player == null) return;
        _player.MediaOpened -= OnMediaOpened;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        try
        {
            _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            _player.PlaybackSession.PositionChanged -= OnPositionChanged;
            _player.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
        }
        catch
        {
            // session may be gone with a disposed player
        }
    }

    private void ApplyLocalization()
    {
        SpeedButton.Visibility = Visibility.Visible;
        BufferingText.Text = Loc.Get("Player.Buffering");
        ToolTipService.SetToolTip(FullscreenButton, Loc.Get("Player.Fullscreen"));
        ToolTipService.SetToolTip(ZoomButton, Loc.Get("Player.Zoom"));
        ToolTipService.SetToolTip(ExternalButton, Loc.Get("Player.External"));
        ToolTipService.SetToolTip(PinButton, Loc.Get("Player.Pin"));
        ToolTipService.SetToolTip(CloseButton, Loc.Get("Player.Close"));
        MirrorButton.Content = Loc.Get("Settings.AutoMirror");
    }

    /// <summary>
    /// Chromeless window setup: TopBar is the drag region, chrome policy
    /// (no system title bar) + pin state are (re-)applied. Call on
    /// navigate and after every fullscreen transition.
    /// </summary>
    private void ApplyWindowChrome()
    {
        var window = Host;
        if (window == null) return;
        try { window.SetTitleBar(TopBar); }
        catch (Exception ex) { App.TryLog(ex); }
        if (window is PlayerWindow pw) pw.ApplyChromePolicy();
        ApplyPin();
    }

    private void ApplyPin()
    {
        try
        {
            if (Host?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
            {
                overlapped.IsAlwaysOnTop = _isPinned;
            }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        PinButton.IsChecked = _isPinned;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        _isPinned = !_isPinned;
        ApplyPin();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try { (HostWindow ?? Host)?.Close(); }
        catch (Exception ex) { App.TryLog(ex); }
    }

    // ---------- custom title-bar dragging (TopBar) ----------
    // Dragging is native only (Window.SetTitleBar): a manual Move() per
    // pointer event fights the native drag and causes stutter, so there
    // is intentionally no manual moving code here. SetTitleBar must be
    // (re-)applied after window activation — see Loaded + ApplyWindowChrome.

    private static bool IsTopBarInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current != null)
        {
            if (current is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase
                || current is ComboBox
                || current is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem
                || current is Microsoft.UI.Xaml.Controls.Primitives.Thumb
                || current is Slider)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void TopBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsTopBarInteractiveSource(e.OriginalSource)) return;
        ToggleFullscreen();
    }

    private void ApplySpeed()
    {
        if (_player == null) return;
        try
        {
            _player.PlaybackRate = SettingsService.Instance.Speed;
        }
        catch
        {
            // rate may not be supported for this audio track
        }
    }

    private async Task LoadVideoAsync(bool seekToSaved, string? qualityOverride = null)
    {
        if (_launch == null) return;

        SetupPlayer();
        if (_player == null) return;

        _switching = true;
        _stallTimes.Clear();
        _bufferStart = null;
        ShowLoading(Loc.Get("Common.Loading"));
        BigPlayButton.Visibility = Visibility.Collapsed;

        try
        {
            _video = await RezkaService.Instance.Client.GetMovieVideoAsync(
                _launch.Voice, _launch.Season, _launch.Episode, _launch.Details.Favs);

            App.TryLog(new Exception($"[Player] Got video: tracks={_video.Videos.Count} subs={_video.Subtitles.Count}"));

            var track = _video.GetClosestTo(qualityOverride ?? _quality) ?? _video.GetMaxQuality();
            if (track == null || track.Urls.Count == 0)
            {
                ShowStatus(Loc.Get("Details.NoVideo"));
                return;
            }

            _quality = track.Quality;
            BuildQualityBox();
            BuildSubtitlesBox(_video);

            long seekMs;
            if (seekToSaved)
            {
                seekMs = GetSavedPositionMs();
            }
            else if (_player != null)
            {
                seekMs = (long)_player.PlaybackSession.Position.TotalMilliseconds;
            }
            else
            {
                seekMs = 0;
            }

            _pendingSeekMs = seekMs;
            var streamUrl = track.Urls[0];
            App.TryLog(new Exception($"[Player] Track: {track.Quality} urls={track.Urls.Count} account={track.NeedAccount} premium={track.NeedPremium}"));
            App.TryLog(new Exception($"[Player] Stream URL: {streamUrl}"));

            MediaSource source;
            if (streamUrl.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                var hls = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(streamUrl));
                if (hls.Status != AdaptiveMediaSourceCreationStatus.Success || hls.MediaSource == null)
                {
                    throw new RezkaException(RezkaError.Parse, "HLS failed: " + hls.Status);
                }

                source = MediaSource.CreateFromAdaptiveMediaSource(hls.MediaSource);
            }
            else
            {
                source = MediaSource.CreateFromUri(new Uri(streamUrl));
            }

            _currentSource = source;
            _mediaOpened = false;
            var gen = ++_loadGen;

            _playbackItem = new MediaPlaybackItem(source);
            var player = _player;
            if (player == null) return;
            player.Source = _playbackItem;
            // Setting Source resets PlaybackRate to 1x: re-apply saved speed,
            // otherwise UI shows 2x while video plays at 1x.
            ApplySpeed();
            App.TryLog(new Exception($"[Player] Source set, pending seek={_pendingSeekMs}"));
            _ = WatchOpenTimeoutAsync(gen);
        }
        catch (RezkaException ex)
        {
            App.TryLog(ex);
            ShowStatus(RezkaService.Instance.ErrorText(ex),
                ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            ShowStatus(Loc.Get("Error.Network"), showMirrorButton: true);
        }
        finally
        {
            _switching = false;
            _endReached = false;
        }
    }

    private void BuildSubtitles(MediaSource source, MovieVideo video)
    {
        _attachedSubtitles.Clear();
        foreach (var sub in video.Subtitles)
        {
            if (!Uri.TryCreate(sub.Link, UriKind.Absolute, out var uri)) continue;
            try
            {
                var tts = TimedTextSource.CreateFromUri(uri);
                tts.Resolved += (s, e) =>
                {
                    if (e.Error != null)
                    {
                        App.TryLog(new Exception("[Player] Subtitle resolve failed: " + sub.Name));
                    }
                    else
                    {
                        foreach (var track in e.Tracks)
                        {
                            track.Label = sub.Name;
                        }
                    }
                };
                source.ExternalTimedTextSources.Add(tts);
                _attachedSubtitles.Add(sub);
            }
            catch (Exception ex)
            {
                App.TryLog(new Exception("[Player] Subtitle attach failed: " + ex.Message));
            }
        }
    }

    private long GetSavedPositionMs()
    {
        if (_launch == null) return 0;
        var numericId = DetailsPage.ExtractNumericId(_launch.Details.Id);
        var saved = PositionService.Instance.Get(
            numericId ?? _launch.Details.Id,
            _launch.Voice.TranslatorId,
            _launch.Season?.SeasonId,
            _launch.Episode?.EpisodeId);
        if (saved is { DurationSeconds: > 10 } && saved.PositionSeconds > 10 &&
            saved.PositionSeconds < saved.DurationSeconds - 30)
        {
            return (long)(saved.PositionSeconds * 1000);
        }

        return 0;
    }

    private void BuildQualityBox()
    {
        if (_video == null) return;
        QualityBox.SelectionChanged -= QualityBox_SelectionChanged;
        QualityBox.Items.Clear();
        foreach (var q in _video.Videos)
        {
            var label = q.Quality + (q.NeedAccount ? " 🔒" : "") + (q.NeedPremium ? " ★" : "");
            QualityBox.Items.Add(new ComboBoxItem { Content = label, Tag = q.Quality, IsEnabled = !q.NeedAccount });
        }

        for (var i = 0; i < QualityBox.Items.Count; i++)
        {
            if (QualityBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == _quality)
            {
                QualityBox.SelectedIndex = i;
                break;
            }
        }

        QualityBox.SelectionChanged += QualityBox_SelectionChanged;
    }

    private void BuildSubtitlesBox(MovieVideo video)
    {
        SubtitlesBox.SelectionChanged -= SubtitlesBox_SelectionChanged;
        SubtitlesBox.Items.Clear();
        SubtitlesBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Common.Subtitles") + ": " + Loc.Get("Player.SubtitlesOff"), Tag = null });

        foreach (var sub in video.Subtitles)
        {
            SubtitlesBox.Items.Add(new ComboBoxItem { Content = sub.Name, Tag = sub });
        }

        SubtitlesBox.SelectedIndex = 0;
        SubtitlesBox.SelectionChanged += SubtitlesBox_SelectionChanged;
    }

    private void BuildSpeedMenu()
    {
        if (SpeedButton.Flyout is MenuFlyout existing)
        {
            existing.Items.Clear();
        }

        var flyout = new MenuFlyout();
        foreach (var speed in Speeds)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x",
                IsChecked = Math.Abs(speed - SettingsService.Instance.Speed) < 0.001,
                Tag = speed,
            };
            item.Click += (_, _) =>
            {
                foreach (var other in flyout.Items.OfType<ToggleMenuFlyoutItem>())
                {
                    other.IsChecked = ReferenceEquals(other, item);
                }

                var rate = (double)item.Tag;
                SettingsService.Instance.Speed = rate;
                SettingsService.Instance.Save();
                SpeedText.Text = rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
                ApplySpeed();
            };
            flyout.Items.Add(item);
        }

        SpeedButton.Flyout = flyout;
        SpeedText.Text = SettingsService.Instance.Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
    }

    private void SpeedButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        if (SpeedButton.Flyout is MenuFlyout flyout)
        {
            flyout.ShowAt(SpeedButton);
        }
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        ToggleFullscreen();
    }

    private async void ExternalButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        var url = _video?.Videos.FirstOrDefault(v => v.Quality == _quality)?.Urls.FirstOrDefault()
            ?? _video?.GetMaxQuality()?.Urls.FirstOrDefault();
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            ShowStatus(Loc.Get("Details.NoVideo"));
            return;
        }

        // Copy link so user can paste it into VLC/mpv/Browser manually.
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(uri.ToString());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            ShowStatus(Loc.Get("Player.ExternalFail"));
        }
    }

    private async void QualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switching || _video == null || _launch == null) return;
        if (QualityBox.SelectedItem is not ComboBoxItem { Tag: string quality } || quality == _quality) return;

        var track = _video.Videos.FirstOrDefault(v => v.Quality == quality);
        if (track == null) return;
        if (track.NeedAccount && !RezkaService.Instance.IsLoggedIn)
        {
            ShowStatus(Loc.Get("Player.NeedLogin"));
            BuildQualityBox();
            return;
        }

        if (track.NeedPremium)
        {
            ShowStatus(Loc.Get("Player.NeedPremium"));
            BuildQualityBox();
            return;
        }

        _quality = quality;
        await LoadVideoAsync(seekToSaved: false, qualityOverride: quality);
    }

    private void SubtitlesBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_playbackItem == null) return;
        if (SubtitlesBox.SelectedItem is not ComboBoxItem selected) return;

        try
        {
            var tracks = _playbackItem.TimedMetadataTracks;
            for (var i = 0; i < tracks.Count; i++)
            {
                tracks.SetPresentationMode((uint)i, TimedMetadataTrackPresentationMode.Disabled);
            }

            if (selected.Tag is MovieSubtitle sub)
            {
                var idx = _attachedSubtitles.FindIndex(s => s.Link == sub.Link);
                if (idx >= 0 && idx < tracks.Count)
                {
                    tracks.SetPresentationMode((uint)idx, TimedMetadataTrackPresentationMode.PlatformPresented);
                }
            }
        }
        catch (Exception ex)
        {
            App.TryLog(new Exception("[Player] Subtitle select failed: " + ex.Message));
        }
    }

    // ---------- playback events ----------

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _mediaOpened = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_tearingDown) return;
                var duration = sender.PlaybackSession.NaturalDuration;
            if (duration > TimeSpan.Zero)
            {
                TotalTimeText.Text = FormatTime((long)duration.TotalMilliseconds);
            }

            App.TryLog(new Exception($"[Player] MediaOpened duration={duration}"));
            if (_pendingSeekMs > 0)
            {
                var seek = _pendingSeekMs;
                _pendingSeekMs = 0;
                try
                {
                    sender.PlaybackSession.Position = TimeSpan.FromMilliseconds(seek);
                }
                catch
                {
                }
            }

            if (_currentSource != null && _video != null)
            {
                BuildSubtitles(_currentSource, _video);
            }

            // Crash watchdog: rewritten on every clean stop, so a leftover
            // marker at next launch means playback died with the process.
            WritePlaybackMarker(_launch?.Details.Name ?? "");

            try
            {
                sender.Play();
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
        });
    }

    private static string PlaybackMarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HDREZKA", "playback_active.txt");

    private static void WritePlaybackMarker(string title)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlaybackMarkerPath)!);
            File.WriteAllText(PlaybackMarkerPath, $"{title}\n{DateTime.UtcNow:o}");
        }
        catch
        {
        }
    }

    private static void ClearPlaybackMarker()
    {
        try
        {
            if (File.Exists(PlaybackMarkerPath)) File.Delete(PlaybackMarkerPath);
        }
        catch
        {
        }
    }

    internal static string? TakePlaybackMarker()
    {
        try
        {
            if (!File.Exists(PlaybackMarkerPath)) return null;
            var firstLine = File.ReadAllLines(PlaybackMarkerPath).FirstOrDefault()?.Trim();
            File.Delete(PlaybackMarkerPath);
            return string.IsNullOrEmpty(firstLine) ? "?" : firstLine;
        }
        catch
        {
            return null;
        }
    }

    private async Task WatchOpenTimeoutAsync(int gen)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (gen != _loadGen || _mediaOpened || _player == null) return;
            App.TryLog(new Exception("[Player] Open timeout: no MediaOpened in 20s"));
            ShowStatus(Loc.Get("Error.Network"));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private void OnPositionChanged(MediaPlaybackSession sender, object args)
    {
        if (_tearingDown || _switching) return;

        var now = DateTime.UtcNow;
        if ((now - _lastTimeChanged).TotalMilliseconds < 250) return;
        _lastTimeChanged = now;

        long posMs, totalMs;
        try
        {
            posMs = (long)sender.Position.TotalMilliseconds;
            totalMs = (long)sender.NaturalDuration.TotalMilliseconds;
        }
        catch
        {
            return; // session dying (dispose race) — ignore
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_tearingDown) return;
                TotalTimeText.Text = FormatTime(totalMs);
                CurrentTimeText.Text = FormatTime(posMs);
                if (!_seeking && totalMs > 0)
                {
                    SeekSlider.Value = posMs * 1000.0 / totalMs;
                }
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
        });
    }

    private void OnNaturalDurationChanged(MediaPlaybackSession sender, object args)
    {
        if (_tearingDown) return;
        long totalMs;
        try
        {
            totalMs = (long)sender.NaturalDuration.TotalMilliseconds;
        }
        catch
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_tearingDown) return;
                if (totalMs > 0) TotalTimeText.Text = FormatTime(totalMs);
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
        });
    }

    private readonly List<DateTime> _stallTimes = new();

    /// <summary>When the current buffering episode started (null = not buffering).</summary>
    private DateTime? _bufferStart;

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var state = sender.PlaybackState;
        var posMs = 0L;
        try
        {
            posMs = (long)sender.Position.TotalMilliseconds;
        }
        catch
        {
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_tearingDown) return;
                switch (state)
                {
                case MediaPlaybackState.Playing:
                    PlayPauseIcon.Glyph = "\uE769";
                    BigPlayIcon.Glyph = "\uE769";
                    BigPlayButton.Visibility = Visibility.Collapsed;
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    _hideTimer.Start();
                    UpdateDisplayRequest(true);
                    if (_bufferStart != null)
                    {
                        // Buffering episode over: only a long stall (>4s)
                        // counts. Short rebuffers from seeks/pauses are normal.
                        var bufferingFor = DateTime.UtcNow - _bufferStart.Value;
                        _bufferStart = null;
                        if (bufferingFor.TotalSeconds >= 4 && !_switching)
                        {
                            MaybeDropQualityOnStall();
                        }
                    }

                    break;
                case MediaPlaybackState.Paused:
                    PlayPauseIcon.Glyph = "\uE768";
                    BigPlayIcon.Glyph = "\uE768";
                    BigPlayButton.Opacity = 1;
                    BigPlayButton.Visibility = Visibility.Visible;
                    UpdateDisplayRequest(false);
                    _bufferStart = null;
                    ShowControls();
                    break;
                case MediaPlaybackState.Buffering:
                    BufferingRing.IsActive = true;
                    BufferingStatusRow.Visibility = Visibility.Visible;
                    ShowControls();
                    _bufferStart ??= DateTime.UtcNow;
                    break;
                case MediaPlaybackState.Opening:
                    break;
            }

            if (state != MediaPlaybackState.Buffering)
            {
                BufferingRing.IsActive = false;
                BufferingStatusRow.Visibility = Visibility.Collapsed;
            }

            App.TryLog(new Exception($"[Player] State: {state} Time: {posMs}"));
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
        });
    }

    private void MaybeDropQualityOnStall()
    {
        var now = DateTime.UtcNow;
        _stallTimes.Add(now);
        _stallTimes.RemoveAll(t => (now - t).TotalSeconds > 60);
        if (_stallTimes.Count < 3) return;
        _stallTimes.Clear();

        if (_video == null || _player == null || _launch == null || _switching) return;

        var ordered = _video.Videos.Where(v => !v.NeedAccount && !v.NeedPremium).ToList();
        var idx = ordered.FindIndex(v => v.Quality == _quality);
        if (idx <= 0) return;

        var next = ordered[idx - 1];
        App.TryLog(new Exception($"[Player] Auto quality drop: {_quality} -> {next.Quality}"));
        _quality = next.Quality;
        _ = LoadVideoAsync(seekToSaved: false, qualityOverride: next.Quality);
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        UpdateDisplayRequest(false);
        _bufferStart = null;
        DispatcherQueue.TryEnqueue(async () =>
        {
            _endReached = true;
            SavePosition(final: true);
            if (await TryNextEpisodeAsync(auto: true))
            {
                return;
            }

            GoBack();
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs e)
    {
        UpdateDisplayRequest(false);
        _bufferStart = null;
        _pendingSeekMs = 0;
        App.TryLog(new Exception($"[Player] MediaFailed: {e.Error} {e.ErrorMessage} code={e.ExtendedErrorCode}"));
        DispatcherQueue.TryEnqueue(() => ShowStatus(Loc.Get("Player.StreamFailed"), showMirrorButton: true));
    }

    // ---------- controls ----------

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        if (_player == null) return;
        // Error state (nothing loaded): big button tap retries same mirror.
        if (_playbackItem == null && !_switching && _launch != null)
        {
            _ = LoadVideoAsync(seekToSaved: false);
            return;
        }

        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
        else _player.Play();
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        await TryPrevEpisodeAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        await TryNextEpisodeAsync(auto: false);
    }

    private async Task<bool> TryNextEpisodeAsync(bool auto)
    {
        if (_launch?.Seasons == null || _launch.Season == null) return false;

        var seasonIdx = _launch.Seasons.ToList().FindIndex(s => s.SeasonId == _launch.Season!.SeasonId);
        var episodes = _launch.Season.Episodes;
        var epIdx = episodes.ToList().FindIndex(ep => ep.EpisodeId == _launch.Episode?.EpisodeId);

        MovieSeason? nextSeason = null;
        MovieEpisode? nextEpisode = null;

        if (epIdx >= 0 && epIdx + 1 < episodes.Count)
        {
            nextSeason = _launch.Season;
            nextEpisode = episodes[epIdx + 1];
        }
        else if (seasonIdx >= 0 && seasonIdx + 1 < _launch.Seasons.Count)
        {
            nextSeason = _launch.Seasons[seasonIdx + 1];
            nextEpisode = nextSeason.Episodes.FirstOrDefault();
        }

        if (nextEpisode == null) return false;

        SavePosition(final: true);
        _launch = _launch with { Season = nextSeason, Episode = nextEpisode };
        UpdateEpisodeText();
        await LoadVideoAsync(seekToSaved: true);
        return true;
    }

    private async Task<bool> TryPrevEpisodeAsync()
    {
        if (_launch?.Seasons == null || _launch.Season == null) return false;

        var seasonIdx = _launch.Seasons.ToList().FindIndex(s => s.SeasonId == _launch.Season!.SeasonId);
        var episodes = _launch.Season.Episodes;
        var epIdx = episodes.ToList().FindIndex(ep => ep.EpisodeId == _launch.Episode?.EpisodeId);

        MovieSeason? prevSeason = null;
        MovieEpisode? prevEpisode = null;

        if (epIdx > 0)
        {
            prevSeason = _launch.Season;
            prevEpisode = episodes[epIdx - 1];
        }
        else if (seasonIdx > 0)
        {
            prevSeason = _launch.Seasons[seasonIdx - 1];
            prevEpisode = prevSeason.Episodes.LastOrDefault();
        }

        if (prevEpisode == null) return false;

        _launch = _launch with { Season = prevSeason, Episode = prevEpisode };
        UpdateEpisodeText();
        await LoadVideoAsync(seekToSaved: true);
        return true;
    }

    private void UpdateEpisodeText()
    {
        if (_launch?.Season != null && _launch.Episode != null)
        {
            EpisodeChip.Visibility = Visibility.Visible;
            EpisodeText.Text = $"{Loc.Get("Common.Season")} {_launch.Season.Name} \u00B7 {Loc.Get("Common.Episode")} {_launch.Episode.Name}";
        }
        else
        {
            EpisodeChip.Visibility = Visibility.Collapsed;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        GoBack();
    }

    private void GoBack()
    {
        SavePosition();
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
        }
        else if (HostWindow != null)
        {
            // Hosted in a separate player window: back closes it.
            try { HostWindow.Close(); } catch (Exception ex) { App.TryLog(ex); }
        }
    }

    // ---------- seek/volume ----------

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => _seeking = true;

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_player?.PlaybackSession.NaturalDuration.TotalMilliseconds is double total && total > 0)
        {
            _player.PlaybackSession.Position = TimeSpan.FromMilliseconds(total * SeekSlider.Value / 1000.0);
        }

        _seeking = false;
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_player != null)
        {
            _player.Volume = Math.Clamp(VolumeSlider.Value, 0, 100) / 100.0;
            SettingsService.Instance.Volume = (int)Math.Clamp(VolumeSlider.Value, 0, 100);
            SettingsService.Instance.Save();
            UpdateMuteIcon();
        }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        if (_player == null) return;
        if (_player.IsMuted)
        {
            _player.IsMuted = false;
            VolumeSlider.Value = Math.Clamp(_wasMuted, 0, 100);
        }
        else
        {
            _wasMuted = (int)Math.Clamp(VolumeSlider.Value, 0, 100);
            _player.IsMuted = true;
        }

        UpdateMuteIcon();
    }

    private void UpdateMuteIcon()
    {
        if (_player == null) return;
        if (_player.IsMuted || _player.Volume <= 0.001)
        {
            MuteIcon.Glyph = "\uE74F";
        }
        else if (_player.Volume <= 0.5)
        {
            MuteIcon.Glyph = "\uE992";
        }
        else
        {
            MuteIcon.Glyph = "\uE995";
        }
    }

    // ---------- position saving ----------

    internal static void PersistPosition(PlayerLaunch launch, long timeMs, long lengthMs, string quality)
    {
        try
        {
            if (lengthMs <= 0) return;
            var numericId = DetailsPage.ExtractNumericId(launch.Details.Id);
            PositionService.Instance.Save(new WatchPosition(
                numericId ?? launch.Details.Id,
                launch.Voice.TranslatorId,
                launch.Season?.SeasonId,
                launch.Episode?.EpisodeId,
                Math.Max(0, timeMs) / 1000.0,
                lengthMs / 1000.0,
                DateTime.Now,
                null,
                quality,
                launch.Details.Name,
                launch.Details.Poster,
                launch.Details.Id));

            // Series only: remember for new-episode notifications.
            if (launch.Season != null && launch.Episode != null)
            {
                TrackedSeriesService.Touch(launch.Details.Id, launch.Details.Name, launch.Details.Poster);
            }
        }
        catch (Exception ex)
        {
            App.TryLog(new Exception("[Player] PersistPosition failed: " + ex.Message));
        }
    }

    private void SavePosition(bool final = false)
    {
        if (_launch == null || _player == null || _endReached && final) return;

        try
        {
            var time = (long)_player.PlaybackSession.Position.TotalMilliseconds;
            var length = (long)_player.PlaybackSession.NaturalDuration.TotalMilliseconds;
            if (length <= 0) return;

            PersistPosition(_launch, time, length, _quality);

            if (RezkaService.Instance.IsLoggedIn && (DateTime.UtcNow - _lastSiteSave).TotalSeconds > 25)
            {
                _lastSiteSave = DateTime.UtcNow;
                _ = RezkaService.Instance.Client.SaveWatchingAsync(
                    _launch.Voice,
                    _launch.Season,
                    _launch.Episode,
                    (int)(time / 1000),
                    (int)(length / 1000));
            }
        }
        catch
        {
            // best effort
        }
    }

    private static readonly Stretch[] ZoomModes = { Stretch.Uniform, Stretch.UniformToFill, Stretch.Fill };
    private static readonly string[] ZoomLabels = { "Auto", "Fill", "Stretch" };
    private int _zoomIndex;

    private void ZoomButton_Click(object sender, RoutedEventArgs e)
    {
        _zoomIndex = (_zoomIndex + 1) % ZoomModes.Length;
        PlayerElement.Stretch = ZoomModes[_zoomIndex];
        ZoomText.Text = ZoomLabels[_zoomIndex];
        App.TryLog(new Exception("[Player] Zoom: " + ZoomLabels[_zoomIndex]));
    }

    // ---------- overlay + fullscreen ----------

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
        _hideTimer.Start();
    }

    private void TapCatcher_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_player == null) return;
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
        else _player.Play();
    }

    private void TapCatcher_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFullscreen();

    private void TapCatcher_RightTapped(object sender, RightTappedRoutedEventArgs e) => ToggleFullscreen();

    private void ReturnFocus() => TapCatcher.Focus(FocusState.Programmatic);

    private static void FadeElement(UIElement element, bool show)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(show ? 180 : 320)),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private bool IsPlaying() =>
        _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    private void ShowControls()
    {
        TopBar.IsHitTestVisible = true;
        BottomBar.IsHitTestVisible = true;
        FadeElement(TopBar, show: true);
        FadeElement(BottomBar, show: true);
        ShowSystemCursor();
        if (!IsPlaying()) return;
        _hideTimer.Start();
    }

    private void HideControls()
    {
        if (!IsPlaying()) return;
        _hideTimer.Stop();
        FadeElement(TopBar, show: false);
        FadeElement(BottomBar, show: false);
        // Invisible bars must not swallow taps: TapCatcher below handles them.
        TopBar.IsHitTestVisible = false;
        BottomBar.IsHitTestVisible = false;
        if (_isFullscreen) HideSystemCursor();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private static void HideSystemCursor()
    {
        // ShowCursor is counter-based: force below zero (capped).
        try { for (var i = 0; i < 16 && ShowCursor(false) >= 0; i++) { } } catch { }
    }

    private static void ShowSystemCursor()
    {
        try { for (var i = 0; i < 16 && ShowCursor(true) < 0; i++) { } } catch { }
    }

    private void ToggleFullscreen()
    {
        var window = Host;
        if (window == null) return;
        var appWindow = window.AppWindow;

        if (!_isFullscreen)
        {
            _savedBounds = new Windows.Graphics.RectInt32(
                appWindow.Position.X, appWindow.Position.Y,
                appWindow.Size.Width, appWindow.Size.Height);
            App.TryLog(new Exception($"[Player] FS enter: kind={appWindow.Presenter.Kind} bounds={appWindow.Size.Width}x{appWindow.Size.Height}"));
            try
            {
                // NOTE: intentionally NOT touching border/titlebar flags here.
                // FullScreen presenter hides all chrome by itself; flipping
                // SetBorderAndTitleBar around maximize/restore leaves WinAppSDK
                // in a broken state (no title bar, floating X over content).
                // The player window also has min/max disabled, so there is
                // nothing else to hide.
                try
                {
                    appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                }
                catch (Exception ex)
                {
                    App.TryLog(new Exception("[Player] FS presenter failed, borderless fallback: " + ex.Message));
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    var wid = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                        wid, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                    appWindow.MoveAndResize(area.OuterBounds);
                }

                App.TryLog(new Exception($"[Player] FS on: kind={appWindow.Presenter.Kind} bounds={appWindow.Size.Width}x{appWindow.Size.Height}@{appWindow.Position.X},{appWindow.Position.Y}"));
                try { ApplyWindowChrome(); } catch { }
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }

            _isFullscreen = true;
            FullscreenIcon.Glyph = "\uE73F";
            // Give immediate UI feedback + restart the auto-hide timer.
            try { ShowControls(); } catch { }
        }
        else
        {
            ExitFullscreen();
        }
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        _isFullscreen = false;
        try
        {
            FullscreenIcon.Glyph = "\uE740";
        }
        catch { }

        var window = Host;
        if (window == null) return;
        var appWindow = window.AppWindow;
        try
        {
            if (appWindow.Presenter.Kind != Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped)
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
            }

            // SetPresenter creates a new presenter instance, so re-apply
            // the chromeless policy + drag region + pin on it.
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter)
            {
                try { ApplyWindowChrome(); } catch { }
            }

            var b = _savedBounds;
            if (b.Width > 0 && b.Height > 0) appWindow.MoveAndResize(b);

            try { window.Activate(); } catch { }

            App.TryLog(new Exception($"[Player] FS off: kind={appWindow.Presenter.Kind} bounds={appWindow.Size.Width}x{appWindow.Size.Height}"));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            try { appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped); } catch { }
        }
        finally
        {
            // Controls may have auto-hidden while fullscreen (opacity 0):
            // exiting via keyboard/button produces no pointer move,
            // so bring the top bar (back/title/quality) back explicitly.
            try { ShowControls(); } catch { }
        }
    }

    private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Space:
                PlayPauseButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case VirtualKey.Left when _player != null:
            {
                var pos = _player.PlaybackSession.Position.TotalMilliseconds;
                _player.PlaybackSession.Position = TimeSpan.FromMilliseconds(Math.Max(0, pos - 10_000));
                e.Handled = true;
                break;
            }

            case VirtualKey.Right when _player != null:
            {
                var session = _player.PlaybackSession;
                var totalMs = session.NaturalDuration.TotalMilliseconds;
                var targetMs = session.Position.TotalMilliseconds + 10_000;
                if (totalMs > 0) targetMs = Math.Min(totalMs, targetMs);
                session.Position = TimeSpan.FromMilliseconds(Math.Max(0, targetMs));
                e.Handled = true;
                break;
            }
            case VirtualKey.Up:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                e.Handled = true;
                break;
            case VirtualKey.Escape when _isFullscreen:
                ExitFullscreen();
                e.Handled = true;
                break;
            case VirtualKey.F:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case VirtualKey.N:
                await TryNextEpisodeAsync(auto: false);
                e.Handled = true;
                break;
            case VirtualKey.P:
                await TryPrevEpisodeAsync();
                e.Handled = true;
                break;
        }
    }

    // ---------- status ----------

    private void ShowLoading(string message)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingRing.Visibility = Visibility.Visible;
        MirrorButton.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
    }

    private void ShowStatus(string message, bool showMirrorButton = false)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingRing.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        MirrorButton.Visibility = showMirrorButton ? Visibility.Visible : Visibility.Collapsed;
        BigPlayButton.Visibility = Visibility.Visible;
        BigPlayButton.Opacity = 1;
    }

    private async void MirrorButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnFocus();
        MirrorButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var applied = await MirrorService.PickAndApplyAsync(Content.XamlRoot, DispatcherQueue, cts.Token);
            if (applied != null)
            {
                await LoadVideoAsync(seekToSaved: false);
            }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
        finally
        {
            MirrorButton.IsEnabled = true;
        }
    }

    private static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }
}
