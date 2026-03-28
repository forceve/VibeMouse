using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Vibemouse;

/// <summary>
/// Represents one configurable action binding.
/// </summary>
public class CommandEntry
{
    public string Label   { get; set; } = "";
    public string Type    { get; set; } = "kb";   // "kb" | "ms"
    public string Key     { get; set; } = "";
    public string IpcName { get; set; } = "";
}

public partial class MainWindow : Window
{
    // ── UI State ─────────────────────────────────────────────────────────────
    private bool _listenerOn   = false;
    private int  _captureIndex = -1;
    private bool _capturingKb  = false;
    private bool _capturingMs  = false;

    // ── Mouse gesture state ──────────────────────────────────────────────────
    private readonly HashSet<string>          _heldBtns  = new();  // currently held
    private readonly HashSet<string>          _peakBtns  = new();  // all buttons pressed this gesture
    private KeyModifiers                       _gestureMods;
    private Point                              _gestureOrigin;
    private bool                               _gestureActive;
    private readonly Avalonia.Media.PointCollection _trailPoints = new();

    // ── Button name lookup ───────────────────────────────────────────────────
    private static readonly Dictionary<PointerUpdateKind, string> KindToBtn = new()
    {
        [PointerUpdateKind.LeftButtonPressed]    = "LMB",
        [PointerUpdateKind.LeftButtonReleased]   = "LMB",
        [PointerUpdateKind.MiddleButtonPressed]  = "MMB",
        [PointerUpdateKind.MiddleButtonReleased] = "MMB",
        [PointerUpdateKind.RightButtonPressed]   = "RMB",
        [PointerUpdateKind.RightButtonReleased]  = "RMB",
        [PointerUpdateKind.XButton1Pressed]      = "X1",
        [PointerUpdateKind.XButton1Released]     = "X1",
        [PointerUpdateKind.XButton2Pressed]      = "X2",
        [PointerUpdateKind.XButton2Released]     = "X2",
    };

    // Canonical display order for multi-button combos
    private static readonly string[] BtnOrder = ["LMB", "RMB", "MMB", "X1", "X2"];

    // Dead zone for the recording UI's 180 × 180 gesture area.
    // (The actual listener uses 200 screen-px; this is scaled to the visual box.)
    private const double GestureDeadZone = 60.0;

    // ── Commands ─────────────────────────────────────────────────────────────
    private readonly List<CommandEntry> _commands =
    [
        new() { Label = "Voice Transcribe", Type = "kb", Key = "[ Ctrl ] + [ Alt ] + [ V ]", IpcName = "vibemouse.voice_transcribe" },
        new() { Label = "Gesture Area",     Type = "kb", Key = "[ Win ] + [ Tab ]",           IpcName = "vibemouse.gesture_area"    },
        new() { Label = "Command C",        Type = "kb", Key = "[ Ctrl ] + [ Alt ] + [ C ]", IpcName = "vibemouse.command_c"       },
        new() { Label = "Command D",        Type = "ms", Key = "[ RMB ] + [ ↓ ]",            IpcName = "vibemouse.command_d"       },
        new() { Label = "Command E",        Type = "ms", Key = "[ LMB ] + [ RMB ]",          IpcName = "vibemouse.command_e"       },
        new() { Label = "Command F",        Type = "ms", Key = "[ Ctrl ] + [ X1 ]",          IpcName = "vibemouse.command_f"       },
    ];

    // ── Init ─────────────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnGlobalKeyDown;
        GestureTrail.Points = _trailPoints;
        BuildCommandList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TITLE BAR
    // ═════════════════════════════════════════════════════════════════════════

    private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // ═════════════════════════════════════════════════════════════════════════
    // LISTENER TOGGLE
    // ═════════════════════════════════════════════════════════════════════════

    private void ListenerSwitch_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _listenerOn = ListenerSwitch.IsChecked == true;

        ListenerStatusText.Text = _listenerOn ? "Child Process Active" : "IPC commands only";
        ListenerStatusText.Foreground = _listenerOn
            ? SolidColorBrush.Parse("#4ade80")
            : SolidColorBrush.Parse("#888888");

        BuildCommandList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // COMMAND LIST
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildCommandList()
    {
        CommandList.Children.Clear();
        for (int i = 0; i < _commands.Count; i++)
            CommandList.Children.Add(CreateCommandRow(i));
    }

    private Control CreateCommandRow(int i)
    {
        var cmd = _commands[i];
        var idx = i; // capture for closures

        // Row layout
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
        var rowBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6),
            Child = grid,
        };
        rowBorder.PointerEntered += (_, _) => rowBorder.Background = SolidColorBrush.Parse("#0AFFFFFF");
        rowBorder.PointerExited  += (_, _) => rowBorder.Background = Brushes.Transparent;

        // Label
        var label = new TextBlock
        {
            Text = cmd.Label,
            Foreground = SolidColorBrush.Parse("#A0A0A0"),
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        // Right panel
        var right = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        if (_listenerOn)
        {
            // ── Type toggle icon ⌨️ / 🖱️ ──────────────────────────────────
            var icon = new TextBlock
            {
                Text = cmd.Type == "kb" ? "⌨️" : "🖱️",
                FontSize = 13,
                Opacity = 0.35,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                ToolTip = { } // set below
            };
            ToolTip.SetTip(icon, cmd.Type == "kb" ? "Switch to mouse gesture" : "Switch to keyboard");

            icon.PointerEntered += (_, _) => icon.Opacity = 0.9;
            icon.PointerExited  += (_, _) => icon.Opacity = 0.35;
            icon.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _commands[idx].Type = _commands[idx].Type == "kb" ? "ms" : "kb";
                _commands[idx].Key  = "[ — ]";
                BuildCommandList();
            };
            right.Children.Add(icon);

            // ── Active key chip ───────────────────────────────────────────
            var chipText = new TextBlock
            {
                Text = cmd.Key,
                Foreground = SolidColorBrush.Parse("#E0E0E0"),
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            var chip = new Border
            {
                Background      = SolidColorBrush.Parse("#22FFFFFF"),
                BorderBrush     = SolidColorBrush.Parse("#26FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(11, 5),
                MinWidth        = 140,
                Cursor          = new Cursor(StandardCursorType.Hand),
                Child           = chipText,
            };
            chip.PointerEntered += (_, _) =>
            {
                chip.Background  = SolidColorBrush.Parse("#30FFFFFF");
                chip.BorderBrush = SolidColorBrush.Parse("#50FFFFFF");
            };
            chip.PointerExited += (_, _) =>
            {
                chip.Background  = SolidColorBrush.Parse("#22FFFFFF");
                chip.BorderBrush = SolidColorBrush.Parse("#26FFFFFF");
            };
            chip.PointerPressed += (_, _) => StartCapture(idx);
            right.Children.Add(chip);
        }
        else
        {
            // ── Muted IPC name chip ───────────────────────────────────────
            var ipcText = new TextBlock
            {
                Text       = cmd.IpcName,
                Foreground = SolidColorBrush.Parse("#4a4a50"),
                FontSize   = 12,
                FontFamily = new FontFamily("Consolas, Menlo, Courier New"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            right.Children.Add(new Border
            {
                Background      = SolidColorBrush.Parse("#0AFFFFFF"),
                BorderBrush     = SolidColorBrush.Parse("#12FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(11, 5),
                MinWidth        = 140,
                Child           = ipcText,
            });
        }

        return rowBorder;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CAPTURE: ENTRY / EXIT
    // ═════════════════════════════════════════════════════════════════════════

    private void StartCapture(int index)
    {
        _captureIndex = index;
        if (_commands[index].Type == "kb")
        {
            _capturingKb = true;
            KbCaptureOverlay.IsVisible = true;
        }
        else
        {
            _capturingMs = true;
            ResetGestureUI();
            MsCaptureOverlay.IsVisible = true;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // KEYBOARD CAPTURE
    // ═════════════════════════════════════════════════════════════════════════

    private void KbCapture_Cancel(object? sender, RoutedEventArgs e) => EndKbCapture();

    private void EndKbCapture()
    {
        _capturingKb = false;
        _captureIndex = -1;
        KbCaptureOverlay.IsVisible = false;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturingKb) return;

        // Ignore bare modifier keys
        if (e.Key is Key.LeftCtrl  or Key.RightCtrl  or
                     Key.LeftAlt   or Key.RightAlt   or
                     Key.LeftShift or Key.RightShift or
                     Key.LWin      or Key.RWin)
            return;

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("[ Ctrl ]");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))     parts.Add("[ Alt ]");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))   parts.Add("[ Shift ]");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))    parts.Add("[ Win ]");
        parts.Add($"[ {e.Key} ]");

        if (_captureIndex >= 0)
            _commands[_captureIndex].Key = string.Join(" + ", parts);

        EndKbCapture();
        BuildCommandList();
        e.Handled = true;

        // TODO: persist to config file
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MOUSE CAPTURE
    // ═════════════════════════════════════════════════════════════════════════

    private void MsCapture_Cancel(object? sender, RoutedEventArgs e) => EndMsCapture();

    private void EndMsCapture()
    {
        _capturingMs  = false;
        _captureIndex = -1;
        _heldBtns.Clear();
        _peakBtns.Clear();
        _gestureActive = false;
        ResetGestureUI();
        MsCaptureOverlay.IsVisible = false;
    }

    // ── Pointer pressed (first or additional button) ─────────────────────────
    private void GestureArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_capturingMs) return;

        var kind = e.GetCurrentPoint(GestureAreaBorder).Properties.PointerUpdateKind;
        if (!KindToBtn.TryGetValue(kind, out var btn)) return;

        // On the first button down: record origin and capture pointer
        if (!_gestureActive)
        {
            _gestureMods   = e.KeyModifiers;
            _gestureOrigin = e.GetCurrentPoint(GestureAreaBorder).Position;
            _gestureActive = true;
            _trailPoints.Clear();
            _trailPoints.Add(_gestureOrigin);
            e.Pointer.Capture(GestureAreaBorder);
        }

        _heldBtns.Add(btn);
        _peakBtns.Add(btn);
        MsErrorBorder.IsVisible = false;
        RenderHeldPills();
        e.Handled = true;
    }

    // ── Pointer moved: draw trail + show direction indicator ─────────────────
    private void GestureArea_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_capturingMs || !_gestureActive) return;

        var pos = e.GetCurrentPoint(GestureAreaBorder).Position;

        // Clamp for trail rendering within the visual box
        _trailPoints.Add(new Point(
            Math.Clamp(pos.X, 0, 180),
            Math.Clamp(pos.Y, 0, 180)));

        var dx   = pos.X - _gestureOrigin.X;
        var dy   = pos.Y - _gestureOrigin.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist > GestureDeadZone)
        {
            GestureArrow.Text       = DirArrow(dx, dy);
            GestureArrow.Foreground = SolidColorBrush.Parse("#4ade80");
        }
        else
        {
            GestureArrow.Text       = "·";
            GestureArrow.Foreground = SolidColorBrush.Parse("#2a2a2e");
        }
    }

    // ── Pointer released: wait for all buttons up, then commit ───────────────
    private void GestureArea_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_capturingMs) return;

        var kind = e.GetCurrentPoint(GestureAreaBorder).Properties.PointerUpdateKind;
        if (KindToBtn.TryGetValue(kind, out var btn))
            _heldBtns.Remove(btn);

        // Still waiting for remaining held buttons
        if (_heldBtns.Count > 0) return;

        _gestureActive = false;
        e.Pointer.Capture(null);

        var releasePos = e.GetCurrentPoint(GestureAreaBorder).Position;
        var dx   = releasePos.X - _gestureOrigin.X;
        var dy   = releasePos.Y - _gestureOrigin.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        // Canonical button order
        var btns = BtnOrder.Where(b => _peakBtns.Contains(b)).ToList();
        var mods = ModsList(_gestureMods);

        // Reject bare single LMB or RMB (no modifier, no gesture)
        bool isBare = mods.Count == 0
                   && btns.Count == 1
                   && btns[0] is "LMB" or "RMB"
                   && dist <= GestureDeadZone;
        if (isBare)
        {
            MsErrorText.Text        = "LMB and RMB alone are not supported";
            MsErrorBorder.IsVisible = true;
            ResetGestureUI(keepError: true);
            _peakBtns.Clear();
            return;
        }

        // Build key string
        var parts = new List<string>();
        parts.AddRange(mods.Select(m => $"[ {m} ]"));
        parts.AddRange(btns.Select(b => $"[ {b} ]"));
        if (dist > GestureDeadZone)
            parts.Add($"[ {DirArrow(dx, dy)} ]");

        CommitMsCapture(string.Join(" + ", parts));
    }

    private void CommitMsCapture(string keyStr)
    {
        if (_captureIndex >= 0)
            _commands[_captureIndex].Key = keyStr;

        GestureResultText.Text      = keyStr;
        GestureResultText.IsVisible = true;

        // Brief display of result, then close
        DispatcherTimer.RunOnce(() =>
        {
            EndMsCapture();
            BuildCommandList();
            // TODO: persist to config file
        }, TimeSpan.FromMilliseconds(350));
    }

    // ── Gesture UI helpers ────────────────────────────────────────────────────
    private void ResetGestureUI(bool keepError = false)
    {
        GestureArrow.Text       = "·";
        GestureArrow.Foreground = SolidColorBrush.Parse("#2a2a2e");
        GestureResultText.IsVisible = false;
        GestureResultText.Text  = "";
        _trailPoints.Clear();
        HeldPillsPanel.Children.Clear();
        if (!keepError)
            MsErrorBorder.IsVisible = false;
    }

    private void RenderHeldPills()
    {
        HeldPillsPanel.Children.Clear();
        foreach (var m in ModsList(_gestureMods))
            HeldPillsPanel.Children.Add(MakePill($"[ {m} ]", "#A0A0A0", "#14FFFFFF", "#20FFFFFF"));
        foreach (var b in BtnOrder.Where(b => _heldBtns.Contains(b)))
            HeldPillsPanel.Children.Add(MakePill($"[ {b} ]", "#E0E0E0", "#22FFFFFF", "#33FFFFFF"));
    }

    private static Border MakePill(string text, string fg, string bg, string border) =>
        new()
        {
            Background      = SolidColorBrush.Parse(bg),
            BorderBrush     = SolidColorBrush.Parse(border),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(7, 2),
            Margin          = new Thickness(2),
            Child = new TextBlock
            {
                Text       = text,
                Foreground = SolidColorBrush.Parse(fg),
                FontSize   = 11,
                FontWeight = FontWeight.Medium,
            },
        };

    // ═════════════════════════════════════════════════════════════════════════
    // STATIC HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private static string DirArrow(double dx, double dy) =>
        Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? "→" : "←") : (dy > 0 ? "↓" : "↑");

    private static List<string> ModsList(KeyModifiers mods)
    {
        var list = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) list.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt))     list.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Shift))   list.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Meta))    list.Add("Win");
        return list;
    }
}
