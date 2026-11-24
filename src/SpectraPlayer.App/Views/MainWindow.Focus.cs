using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SpectraPlayer.App.Input;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Focus;

namespace SpectraPlayer.App.Views;

// The focus timer: study in timed sessions with breaks. The logic is FocusTimer (Core); this file is the Focus tab in the
// side panel, the little timer in the control bar, the corner display in fullscreen, and what happens when a part ends.
public partial class MainWindow
{
    private const string FocusSetting = "focus";

    private static readonly Brush DotIdleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x56)));

    // For testing only: SPECTRAPLAYER_FOCUS_SPEED=60 makes the timer run 60 times faster (a 25-minute session in 25 seconds).
    private static readonly double FocusSpeed =
        double.TryParse(Environment.GetEnvironmentVariable("SPECTRAPLAYER_FOCUS_SPEED"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var speed) && speed is > 0 and <= 600 ? speed : 1;

    private readonly FocusTimer _focus = new();
    private readonly DispatcherTimer _focusTicker = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _focusClock = Stopwatch.StartNew();
    private TimeSpan _lastFocusTick;
    private int _focusSaveTicks;
    private bool _pausedByFocus;     // we paused the video for a break, so we should start it again afterwards
    private long _hudFlashUntil;     // after a part ends, the corner display stays up for a few seconds
    private FocusHud? _focusHud;

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void InitializeFocus()
    {
        _focus.LoadJson(_library.GetSetting(FocusSetting));

        for (var i = 0; i < FocusTimer.Presets.Count && i < FocusPresets.Children.Count; i++)
            ((RadioButton)FocusPresets.Children[i]).Content = FocusTimer.Presets[i].Label;
        FocusPauseVideoBox.IsChecked = _focus.PauseVideoOnBreaks;
        FocusChimeBox.IsChecked = _focus.ChimeWhenPartEnds;
        FocusCountPlayingBox.IsChecked = _focus.CountOnlyWhilePlaying;

        _focus.Changed += (_, _) => UpdateFocusUi();
        _focus.PartEnded += OnFocusPartEnded;
        _focusTicker.Tick += OnFocusTick;
        _focusTicker.Start();
        UpdateFocusUi();
    }

    private void SaveFocus()
    {
        try { _library.SetSetting(FocusSetting, _focus.ToJson()); }
        catch (Exception ex) when (ex is SqliteException or ObjectDisposedException) { Debug.WriteLine(ex); }
    }

    // ---- time passing ----------------------------------------------------------------------------------------

    private void OnFocusTick(object? sender, EventArgs e)
    {
        var now = _focusClock.Elapsed;
        var elapsed = now - _lastFocusTick;
        _lastFocusTick = now;
        if (elapsed > TimeSpan.FromSeconds(1)) elapsed = TimeSpan.FromSeconds(1); // a stall (or sleep) isn't study time

        if (_focus.Running)
        {
            // "Count only while the video plays" is about focusing; breaks always run (the video is paused for them).
            var videoPlaying = _engine.HasMedia && !_engine.IsPaused;
            var counting = !(_focus.CountOnlyWhilePlaying && _focus.Phase == FocusPhase.Focus && !videoPlaying);
            if (counting) _focus.Tick(FocusSpeed == 1 ? elapsed : elapsed * FocusSpeed);

            if (++_focusSaveTicks >= 240) // about once a minute, so today's total survives a crash
            {
                _focusSaveTicks = 0;
                SaveFocus();
            }
        }

        if (_hudFlashUntil != 0 && Environment.TickCount64 >= _hudFlashUntil)
        {
            _hudFlashUntil = 0;
            UpdateFocusHud();
        }
    }

    private void OnFocusPartEnded(object? sender, FocusPhase ended)
    {
        if (_focus.ChimeWhenPartEnds) SystemSounds.Asterisk.Play();

        if (ended == FocusPhase.Focus)
        {
            if (_engineReady) ShowOsd($"Focus session done. Take a {_focus.Preset.BreakMinutes}-minute break.", 4);
            if (_focus.PauseVideoOnBreaks && _engine.HasMedia && !_engine.IsPaused)
            {
                _engine.Pause();
                _pausedByFocus = true;
            }
        }
        else
        {
            if (_engineReady) ShowOsd("Break over. Back to focus.", 3);
            ReleaseVideoFromBreak();
        }

        _hudFlashUntil = Environment.TickCount64 + 6000;
        SaveFocus();
    }

    /// <summary>The break is over (or cut short): if the timer paused the video for it, play on.</summary>
    private void ReleaseVideoFromBreak()
    {
        if (_pausedByFocus && _engine.HasMedia && _engine.IsPaused) _engine.Play();
        _pausedByFocus = false;
    }

    // ---- what is shown ---------------------------------------------------------------------------------------

    private void UpdateFocusUi()
    {
        var seconds = _focus.SecondsLeft;
        var time = $"{seconds / 60:00}:{seconds % 60:00}";
        var focusing = _focus.Phase == FocusPhase.Focus;
        var arc = (Brush)FindResource(focusing ? "FocusBrush" : "AccentHoverBrush");

        FocusTime.Text = time;
        FocusRing.Fraction = _focus.FractionLeft;
        FocusRing.ArcBrush = arc;
        FocusPhaseLabel.Text = focusing ? "FOCUS" : "BREAK";
        FocusSessionText.Text = _focus.SessionText;

        FocusStartButton.Content = _focus.Running ? "" : ""; // Segoe MDL2: pause / play
        FocusStartButton.ToolTip = (_focus.Running ? "Pause" : "Start") + Paren(PlayerAction.ToggleFocusTimer);

        var dots = FocusDots.Children.OfType<System.Windows.Shapes.Ellipse>().ToList();
        for (var i = 0; i < dots.Count; i++)
        {
            var filled = i < _focus.CompletedSessions;
            var current = i <= _focus.CompletedSessions;
            dots[i].Fill = filled ? (Brush)FindResource("FocusBrush") : Brushes.Transparent;
            dots[i].Stroke = current ? (Brush)FindResource("FocusBrush") : DotIdleBrush;
        }

        for (var i = 0; i < FocusPresets.Children.Count; i++)
            ((RadioButton)FocusPresets.Children[i]).IsChecked = i == _focus.PresetIndex;

        FocusTodayText.Text = _focus.FocusedToday < TimeSpan.FromSeconds(30) && _focus.SessionsToday == 0
            ? "Nothing focused yet today"
            : $"Today: {TimeFormat.FormatTotal(_focus.FocusedToday)} focused · {_focus.SessionsToday} session{(_focus.SessionsToday == 1 ? "" : "s")}";

        // the pill in the control bar, only while there is something to show
        FocusPill.Visibility = _focus.IsActive ? Visibility.Visible : Visibility.Collapsed;
        FocusPillRing.Fraction = _focus.FractionLeft;
        FocusPillRing.ArcBrush = arc;
        FocusPillText.Text = time;

        UpdateFocusHud();
    }

    /// <summary>
    /// The corner display for fullscreen: there whenever the timer is going and the control bar is up, so it comes and goes
    /// with the mouse; for a few seconds after a part ends it stays up on its own.
    /// </summary>
    private void UpdateFocusHud()
    {
        var wanted = _fullscreen && _focus.IsActive;
        if (!wanted)
        {
            _focusHud?.HideNow();
            return;
        }

        if (!NativeMethods.TryGetMonitorBounds(this, out var bounds)) return;
        _focusHud ??= new FocusHud { Owner = this };

        var dpi = VisualTreeHelper.GetDpi(this);
        var seconds = _focus.SecondsLeft;
        _focusHud.SetContent($"{seconds / 60:00}:{seconds % 60:00}",
            _focus.Phase == FocusPhase.Focus ? $"Focus {Math.Min(FocusTimer.SessionsPerRound, _focus.CompletedSessions + 1)} / {FocusTimer.SessionsPerRound}" : "Break",
            _focus.FractionLeft, (Brush)FindResource(_focus.Phase == FocusPhase.Focus ? "FocusBrush" : "AccentHoverBrush"));
        _focusHud.PlaceTopRight((bounds.X + bounds.Width) / dpi.DpiScaleX, bounds.Y / dpi.DpiScaleY, margin: 18);

        var barUp = _overlay is { IsVisible: true };
        _focusHud.FadeTo(barUp || Environment.TickCount64 < _hudFlashUntil);
    }

    // ---- buttons and keys ------------------------------------------------------------------------------------

    private void OnPanelFocusClick(object sender, RoutedEventArgs e) => SwitchPanelView(PanelView.Focus);

    private void OnFocusPillClick(object sender, RoutedEventArgs e) => TogglePanel(PanelView.Focus);

    private void OnFocusStartClick(object sender, RoutedEventArgs e)
    {
        _focus.Toggle();
        SaveFocus();
    }

    private void OnFocusResetClick(object sender, RoutedEventArgs e)
    {
        if (_focus.Phase == FocusPhase.Break) ReleaseVideoFromBreak();
        _focus.Reset();
    }

    private void OnFocusSkipClick(object sender, RoutedEventArgs e)
    {
        if (_focus.Phase == FocusPhase.Break) ReleaseVideoFromBreak();
        _focus.Skip();
    }

    private void OnFocusPresetClick(object sender, RoutedEventArgs e)
    {
        var index = FocusPresets.Children.IndexOf((UIElement)sender);
        if (index < 0) return;
        if (_focus.Phase == FocusPhase.Break) ReleaseVideoFromBreak();
        _focus.SelectPreset(index);
        SaveFocus();
    }

    private void OnFocusOptionClick(object sender, RoutedEventArgs e)
    {
        _focus.PauseVideoOnBreaks = FocusPauseVideoBox.IsChecked == true;
        _focus.ChimeWhenPartEnds = FocusChimeBox.IsChecked == true;
        _focus.CountOnlyWhilePlaying = FocusCountPlayingBox.IsChecked == true;
        SaveFocus();
    }

    /// <summary>The start / pause key: no panel needs to be open, so say what happened on the video.</summary>
    private void ToggleFocusFromKey()
    {
        _focus.Toggle();
        SaveFocus();
        if (_engineReady) ShowOsd(_focus.Running ? $"Focus timer started ({_focus.Preset.Label})" : "Focus timer paused");
    }
}
