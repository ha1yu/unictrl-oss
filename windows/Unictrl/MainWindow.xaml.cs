using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using CanvasRect = Windows.Foundation.Rect;

namespace Unictrl;

public sealed partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly Tray tray;
    private Receiver? receiver;
    private Sender? sender;
    private bool quitting;
    private bool startsAtLogin;
    private bool connected;

    public MainWindow()
    {
        InitializeComponent();
        Localize();
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Unictrl.ico");
        AppWindow.SetIcon(icon);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        TitleIcon.Source = new BitmapImage(new Uri(icon));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = (int)(520 * GetDpiScale());
            presenter.PreferredMinimumHeight = (int)(480 * GetDpiScale());
        }
        var scale = GetDpiScale();
        // The size the user left it at, else the default size.
        var (width, height) = settings.WindowWidth >= 520 && settings.WindowHeight >= 480
            ? (settings.WindowWidth, settings.WindowHeight) : (801.0, 748.0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
        AppWindow.Changed += (_, change) => { if (change.DidSizeChange) RememberSize(); };

        EnabledSwitch.IsOn = settings.Enabled;
        KeyboardBox.SelectedIndex = settings.KeyboardProfile == "mac-positions" ? 1 : 0;
        ShowKeyboardDetail();
        RoleBox.SelectedIndex = settings.Role == "sender" ? 1 : 0;
        ShowRoleDetail();
        MacSideBox.SelectedIndex = settings.MacSide == "left" ? 0 : 1;
        MacOffsetSlider.Value = settings.MacOffsetMm;
        MacSpeedSlider.Value = settings.MacPointerScale;
        UpdateMacOffsetText();
        RestoreDetails();
        StartAtLoginSwitch.IsOn = settings.StartAtLogin;
        AboutText.Text = $"Unictrl {typeof(App).Assembly.GetName().Version?.ToString(3)} · This PC: {Environment.MachineName}";
        ShowPairing(string.IsNullOrEmpty(settings.PairingKey));
        UpdateDetail();
        RenderScreens();

        tray = new Tray(Path.Combine(AppContext.BaseDirectory, "Assets", "UnictrlTray.ico"), settings.ShowTrayIcon);
        TraySwitch.IsOn = settings.ShowTrayIcon;
        TaskbarSwitch.IsOn = settings.ShowInTaskbar;
        Task.Run(StartMenuShortcut.Ensure);
        tray.IsEnabled = () => settings.Enabled;
        tray.StartsAtLogin = () => startsAtLogin;
        tray.OpenRequested += () => DispatcherQueue.TryEnqueue(ShowWindow);
        tray.EnabledToggled += () => DispatcherQueue.TryEnqueue(() => EnabledSwitch.IsOn = !EnabledSwitch.IsOn);
        tray.StartAtLoginToggled += () => DispatcherQueue.TryEnqueue(() => StartAtLoginSwitch.IsOn = !startsAtLogin);
        tray.QuitRequested += () => DispatcherQueue.TryEnqueue(Quit);
        // Closing the window keeps Unictrl running in the notification area; Quit is in the tray menu.
        // With the taskbar icon on, closing minimizes to the taskbar; otherwise it hides (reopen from
        // the notification area or by launching Unictrl again).
        AppWindow.Closing += (_, e) =>
        {
            if (quitting) return;
            e.Cancel = true;
            if (settings.ShowInTaskbar && AppWindow.Presenter is OverlappedPresenter overlapped) overlapped.Minimize();
            else AppWindow.Hide();
        };
        Closed += (_, _) => { receiver?.Dispose(); sender?.Dispose(); tray.Dispose(); };

        if (settings.Enabled) StartPeer(); else SetStatus(Loc.T("Paused", "已暂停"));
        Task.Run(() =>
        {
            startsAtLogin = AutoStart.IsEnabled();
            if (settings.StartAtLogin && !startsAtLogin) SetStartAtLogin(true);
            DispatcherQueue.TryEnqueue(() => StartAtLoginSwitch.IsOn = startsAtLogin);
        });
    }

    /// Picks Chinese labels when Windows runs in Chinese; static XAML text stays English otherwise.
    private void Localize()
    {
        if (!Loc.IsChinese) return;
        ScreensHeader.Text = "屏幕";
        ScreensHint.Text = "拖动屏幕以对应桌面布局；右键可重命名。设置由 Mac 保存。";
        ScreensPlaceholder.Text = "连接到 Mac 后可排列屏幕。";
        DevicesHeader.Text = "设备";
        ConnectionHeader.Text = "连接";
        ConnectionSub.Text = "最近一分钟，实时";
        GeneralHeader.Text = "通用";
        KeyboardTitle.Text = "键盘";
        StartTitle.Text = "随 Windows 启动";
        StartDetail.Text = "登录后在后台运行";
        TrayTitle.Text = "在通知区域显示";
        TrayDetail.Text = "时钟旁的图标";
        TaskTitle.Text = "在任务栏显示";
        TaskDetail.Text = "保留任务栏按钮；关闭窗口即最小化。两个图标都关闭时，从开始菜单打开 Unictrl。";
        MacPosTitle.Text = "Mac 位置";
        MacPosDetail.Text = "Mac 位于这台电脑屏幕的哪一侧";
        HeightTitle.Text = "高度偏移";
        MacSpeedTitle.Text = "Mac 上的指针速度";
        MacSpeedText.Text = "缩放发送给 Mac 的移动量，指针在 Mac 上时生效";
        PairHint.Text = "在 Mac 上点菜单栏的 Unictrl，选择“配对 Windows PC…”，然后输入它显示的代码。";
        PairAgainButton.Content = "重新配对";
        PairButton.Content = "配对";
        CancelPairButton.Content = "取消";
        CodeBox.PlaceholderText = "6 位代码";
        if (RoleBox.Items[0] is ComboBoxItem r0) r0.Content = "Mac 共享给这台电脑";
        if (RoleBox.Items[1] is ComboBoxItem r1) r1.Content = "这台电脑共享给 Mac";
        if (KeyboardBox.Items[0] is ComboBoxItem k0) k0.Content = "Windows 布局";
        if (KeyboardBox.Items[1] is ComboBoxItem k1) k1.Content = "Mac 布局";
        if (MacSideBox.Items[0] is ComboBoxItem s0) s0.Content = "左侧";
        if (MacSideBox.Items[1] is ComboBoxItem s1) s1.Content = "右侧";
        foreach (var toggle in new[] { EnabledSwitch, StartAtLoginSwitch, TraySwitch, TaskbarSwitch })
        {
            toggle.OnContent = "开";
            toggle.OffContent = "关";
        }
    }
    private CancellationTokenSource? sizeSave;

    /// Saves the window size a moment after the user stops resizing.
    private void RememberSize()
    {
        if (!AppWindow.IsVisible) return;
        sizeSave?.Cancel();
        var pending = sizeSave = new CancellationTokenSource();
        var scale = GetDpiScale();
        var size = AppWindow.Size;
        _ = Task.Delay(600, pending.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            settings.WindowWidth = Math.Round(size.Width / scale);
            settings.WindowHeight = Math.Round(size.Height / scale);
            settings.Save();
        }, TaskScheduler.Default);
    }

    /// Started at login: stay out of the way, but keep the taskbar button if the user wants one.
    public void StartInBackground()
    {
        if (!settings.ShowInTaskbar || AppWindow.Presenter is not OverlappedPresenter overlapped) return;
        AppWindow.Show(false);
        overlapped.Minimize(false);
    }

    private void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (settings.ShowTrayIcon == TraySwitch.IsOn) return;
        settings.ShowTrayIcon = TraySwitch.IsOn;
        settings.Save();
        tray.SetVisible(settings.ShowTrayIcon);
    }

    private void OnTaskbarToggled(object sender, RoutedEventArgs e)
    {
        if (settings.ShowInTaskbar == TaskbarSwitch.IsOn) return;
        settings.ShowInTaskbar = TaskbarSwitch.IsOn;
        settings.Save();
    }

    private void ShowWindow()
    {
        AppWindow.Show();
        Activate();
    }

    private void SetStartAtLogin(bool on)
    {
        var ok = on ? AutoStart.Enable() : AutoStart.Disable();
        if (ok) startsAtLogin = on;
        settings.StartAtLogin = startsAtLogin;
        settings.Save();
    }

    private void OnStartAtLoginToggled(object sender, RoutedEventArgs e)
    {
        var on = StartAtLoginSwitch.IsOn;
        if (on == startsAtLogin) return;
        Task.Run(() =>
        {
            SetStartAtLogin(on);
            DispatcherQueue.TryEnqueue(() => StartAtLoginSwitch.IsOn = startsAtLogin);
        });
    }

    private void OnKeyboardChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyboardBox.SelectedItem is not ComboBoxItem { Tag: string profile } || profile == settings.KeyboardProfile) return;
        settings.KeyboardProfile = profile; // the injector reads it for each key
        settings.Save();
        ShowKeyboardDetail();
        Log.Write("keyboard_profile", profile);
    }

    /// What the Mac keyboard's modifier keys do here. "Windows layout" puts the keys where a Windows
    /// keyboard has them (and where dual-labelled keyboards print the Windows names).
    private void ShowKeyboardDetail() =>
        KeyboardDetail.Text = settings.KeyboardProfile == "mac-positions"
            ? Loc.T("⌘ acts as the Windows key and ⌥ as Alt.", "⌘ 充当 Windows 键，⌥ 充当 Alt。")
            : Loc.T("⌥ acts as the Windows key, ⌘ as Alt and the right ⌥ as Ctrl, as on a Windows keyboard.",
                    "⌥ 充当 Windows 键，⌘ 充当 Alt，右 ⌥ 充当 Ctrl，如同 Windows 键盘。");

    private void Quit()
    {
        quitting = true;
        CursorHider.Show("quit");
        receiver?.Dispose();
        sender?.Dispose();
        tray.Dispose();
        Application.Current.Exit();
    }

    // MARK: Status

    private void UpdateDetail()
    {
        var mac = string.IsNullOrEmpty(settings.MacName) ? settings.MacAddress : settings.MacName;
        if (string.IsNullOrEmpty(settings.PairingKey))
        {
            DetailText.Text = Loc.T("Pair with your Mac to use its keyboard and mouse here.", "与 Mac 配对后，在这里使用它的键盘和鼠标。");
            PairTitle.Text = Loc.T("Not paired", "未配对");
            PairDetail.Text = Loc.T("Enter the code your Mac shows", "输入 Mac 显示的代码");
        }
        else
        {
            DetailText.Text = settings.Role == "sender"
                ? (connected ? Loc.T($"Push the pointer past this PC's screen edge to use the keyboard and mouse on {mac}.", $"把指针推过这台电脑的屏幕边缘，即可在 {mac} 上使用键盘和鼠标。")
                    : settings.Enabled ? $"Waiting for {mac} on the local network." : "Turned off on this PC.")
                : (connected ? Loc.T($"Use the Mac's keyboard and mouse by moving the pointer across to this PC.", "把指针移到这台电脑，即可使用 Mac 的键盘和鼠标。")
                    : settings.Enabled ? Loc.T($"Waiting for {mac} on the local network.", $"正在局域网中等待 {mac}…") : Loc.T("Turned off on this PC.", "已在这台电脑上关闭。"));
            PairTitle.Text = mac;
            PairDetail.Text = string.IsNullOrEmpty(settings.MacAddress) ? Loc.T("Paired", "已配对") : $"{Loc.T("Paired", "已配对")} · {settings.MacAddress}";
        }
        StatusBadge.Background = connected ? (Brush)Application.Current.Resources["PineGradientBrush"]
            : (Brush)Application.Current.Resources["ControlAltFillColorQuarternaryBrush"];
        StatusGlyph.Foreground = connected ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void SetStatus(string text)
    {
        tray.SetTooltip($"Unictrl · {text}");
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = text;
            connected = text.StartsWith("Connected", StringComparison.Ordinal);
            UpdateDetail();
        });
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (settings.Enabled == EnabledSwitch.IsOn) return;
        settings.Enabled = EnabledSwitch.IsOn;
        settings.Save();
        StopPeer();
        arrangement = null;
        RenderScreens();
        ShowQuality(null);
        if (settings.Enabled) StartPeer(); else SetStatus(Loc.T("Paused", "已暂停"));
    }

    /// Whatever the role is, only one of these runs at a time.
    private void StopPeer()
    {
        receiver?.Dispose();
        receiver = null;
        sender?.Dispose();
        sender = null;
    }

    private void StartPeer(Action? onConnected = null)
    {
        if (string.IsNullOrEmpty(settings.PairingKey))
        {
            SetStatus(Loc.T("Not paired yet", "尚未配对"));
            return;
        }
        if (settings.Role == "sender")
        {
            var next = new Sender(settings, SetStatus);
            if (onConnected is not null) next.Connected += onConnected;
            sender = next;
            next.Start();
        }
        else
        {
            StartReceiver(onConnected);
        }
    }

    private void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoleBox.SelectedItem is not ComboBoxItem { Tag: string role } || role == settings.Role) return;
        settings.Role = role;
        settings.Save();
        ShowRoleDetail();
        StopPeer();
        arrangement = null;
        RenderScreens();
        ShowQuality(null);
        if (settings.Enabled) StartPeer();
        Log.Write("role", role);
    }

    private void ShowRoleDetail()
    {
        var senderRole = settings.Role == "sender";
        RoleDetail.Text = senderRole
            ? Loc.T("The keyboard and mouse stay on this PC; push the pointer past the screen edge to use them on the Mac.",
                    "键盘和鼠标接在这台电脑上；把指针推过屏幕边缘即可在 Mac 上使用。")
            : Loc.T("The keyboard and mouse stay on the Mac; push the pointer past the Mac's screen edge to use them here.",
                    "键盘和鼠标接在 Mac 上；把指针推过 Mac 的屏幕边缘即可在这边使用。");
        MacSideCard.Visibility = senderRole ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMacSideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacSideBox.SelectedItem is not ComboBoxItem { Tag: string side } || side == settings.MacSide) return;
        settings.MacSide = side; // read by the capture thread for each crossing
        settings.Save();
        Log.Write("mac_side", side);
    }

    private void OnMacOffsetChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (Math.Abs(MacOffsetSlider.Value - settings.MacOffsetMm) < 0.5) return;
        settings.MacOffsetMm = Math.Round(e.NewValue, 0);
        settings.Save();
        UpdateMacOffsetText();
    }

    private void OnMacSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (Math.Abs(MacSpeedSlider.Value - settings.MacPointerScale) < 0.01) return;
        settings.MacPointerScale = Math.Round(e.NewValue, 2); // read by the hook for each move
        settings.Save();
    }

    private void UpdateMacOffsetText() =>
        MacOffsetText.Text = settings.MacOffsetMm == 0
            ? Loc.T("How far the Mac's centre sits below this PC's", "Mac 中心相对这台电脑中心的高度差")
            : Loc.T($"{settings.MacOffsetMm:+0;-0} mm relative to this PC's centre", $"相对这台电脑中心 {settings.MacOffsetMm:+0;-0} mm");

    private void StartReceiver(Action? onConnected = null)
    {
        if (string.IsNullOrEmpty(settings.PairingKey))
        {
            SetStatus(Loc.T("Not paired yet", "尚未配对"));
            return;
        }
        var next = new Receiver(settings, SetStatus);
        next.MacChanged += () => DispatcherQueue.TryEnqueue(UpdateDetail);
        next.ArrangementChanged += info => DispatcherQueue.TryEnqueue(() =>
        {
            if (receiver != next) return;
            arrangement = info;
            RenderScreens();
            ShowNames();
        });
        next.QualityChanged += info => DispatcherQueue.TryEnqueue(() =>
        {
            if (receiver == next) ShowQuality(info);
        });
        if (onConnected is not null) next.Connected += onConnected;
        receiver = next;
        next.Start();
    }

    // MARK: Connection quality

    private List<TipInfo>? shownTips;
    private readonly List<(DateTime At, double Ms)> rttHistory = [];
    private readonly List<(DateTime At, string Source)> hitchHistory = [];
    private static readonly TimeSpan GraphWindow = TimeSpan.FromSeconds(60);

    /// Opens Details and tips as the user left it, then remembers each change.
    private void RestoreDetails()
    {
        DetailsExpander.IsExpanded = settings.ShowConnectionDetails;
        DetailsExpander.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) =>
        {
            if (settings.ShowConnectionDetails == DetailsExpander.IsExpanded) return;
            settings.ShowConnectionDetails = DetailsExpander.IsExpanded;
            settings.Save();
        });
    }

    private void OnRttCanvasSizeChanged(object sender, SizeChangedEventArgs e) => DrawRtt();

    /// Round trips as a line, hitches as ticks along the bottom, the last minute left to right; like the Mac's.
    private void DrawRtt()
    {
        RttCanvas.Children.Clear();
        var width = RttCanvas.ActualWidth;
        var height = RttCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var resources = Application.Current.Resources;
        var now = DateTime.UtcNow;
        const double gutter = 38, tick = 8;
        double plotTop = 2, plotBottom = height - tick - 6, plotWidth = width - gutter;
        var top = Math.Max(20, rttHistory.Count == 0 ? 0 : rttHistory.Max(r => r.Ms) * 1.15);
        double X(DateTime at) => width - (now - at).TotalSeconds / GraphWindow.TotalSeconds * plotWidth;
        double Y(double ms) => plotBottom - Math.Min(ms, top) / top * (plotBottom - plotTop);
        var secondary = (Brush)resources["TextFillColorSecondaryBrush"];

        RttCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = gutter, X2 = width, Y1 = Y(10), Y2 = Y(10), Stroke = secondary, StrokeThickness = 0.5, Opacity = 0.5,
            StrokeDashArray = [3, 3],
        });
        var label = new TextBlock { Text = "10 ms", FontSize = 10, Foreground = secondary, Width = gutter - 6, TextAlignment = TextAlignment.Right };
        Canvas.SetTop(label, Y(10) - 7);
        RttCanvas.Children.Add(label);

        var pine = Windows.UI.Color.FromArgb(255, 0x26, 0x95, 0x7F);
        if (rttHistory.Count > 1)
        {
            var line = new Microsoft.UI.Xaml.Shapes.Polyline { Stroke = new SolidColorBrush(pine), StrokeThickness = 1.3 };
            var area = new Microsoft.UI.Xaml.Shapes.Polygon { Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(40, pine.R, pine.G, pine.B)) };
            area.Points.Add(new Point(X(rttHistory[0].At), plotBottom));
            foreach (var (at, ms) in rttHistory)
            {
                var point = new Point(X(at), Y(ms));
                line.Points.Add(point);
                area.Points.Add(point);
            }
            area.Points.Add(new Point(X(rttHistory[^1].At), plotBottom));
            RttCanvas.Children.Add(area);
            RttCanvas.Children.Add(line);
        }
        RttCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = gutter, X2 = width, Y1 = height - tick / 2, Y2 = height - tick / 2, Stroke = secondary, StrokeThickness = 0.5, Opacity = 0.4,
        });
        foreach (var (at, source) in hitchHistory)
        {
            var x = X(at);
            if (x < gutter) continue;
            var mark = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 2, Height = tick, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(HitchColor(source)) };
            Canvas.SetLeft(mark, x - 1);
            Canvas.SetTop(mark, height - tick);
            RttCanvas.Children.Add(mark);
        }
    }

    /// The Mac's colours for where a hitch came from.
    private static Windows.UI.Color HitchColor(string source) => source switch
    {
        "mouse" => Windows.UI.Color.FromArgb(255, 0xAF, 0x52, 0xDE),
        "mac" => Windows.UI.Color.FromArgb(255, 0x00, 0x7A, 0xFF),
        "network" => Windows.UI.Color.FromArgb(255, 0xFF, 0x95, 0x00),
        _ => Windows.UI.Color.FromArgb(255, 0xFF, 0x2D, 0x55),
    };

    private void ShowQuality(QualityInfo? info)
    {
        ConnectionSection.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        if (info is null)
        {
            shownTips = null;
            TipsPanel.Children.Clear();
            rttHistory.Clear();
            hitchHistory.Clear();
            return;
        }
        var now = DateTime.UtcNow;
        if (info.RttMs is { } current) rttHistory.Add((now, current));
        foreach (var source in info.RecentHitches ?? []) hitchHistory.Add((now, source));
        rttHistory.RemoveAll(r => now - r.At > GraphWindow);
        hitchHistory.RemoveAll(h => now - h.At > GraphWindow);
        GradePeer.Text = string.IsNullOrEmpty(settings.MacName) ? "" : settings.MacName;
        DrawRtt();
        var resources = Application.Current.Resources;
        (GradeText.Text, GradeDot.Fill) = info.Grade switch
        {
            "smooth" => (Loc.T("Smooth", "流畅"), (Brush)resources["SystemFillColorSuccessBrush"]),
            "fair" => (Loc.T("Some hitches", "偶有停顿"), (Brush)resources["SystemFillColorCautionBrush"]),
            "poor" => (Loc.T("Choppy", "卡顿"), (Brush)resources["SystemFillColorCriticalBrush"]),
            _ => (Loc.T("Waiting for movement", "等待指针移动"), (Brush)resources["TextFillColorTertiaryBrush"]),
        };
        RttText.Text = info.RttMs is { } rtt ? $"{rtt:F0} ms" : "";
        RttPeakText.Text = info.RttP95Ms is { } peak ? $"peak {peak:F0} ms" : "";
        var rates = info.HitchesPerMin;
        static string Rate(double value) => value == 0 ? "0" : $"{value:F1}";
        HitchText.Text = info.MovingSeconds < 3 || rates is null
            ? Loc.T("Move the pointer here for a while to measure hitches.", "把指针移到这里一段时间以测量卡顿。")
            : Loc.T($"Hitches per minute of movement, by where they came from: mouse {Rate(rates.Mouse)} · Mac {Rate(rates.Mac)} · network {Rate(rates.Network)} · this PC {Rate(rates.Pc)}",
                    $"每分钟移动中的卡顿次数，按来源：鼠标 {Rate(rates.Mouse)} · Mac {Rate(rates.Mac)} · 网络 {Rate(rates.Network)} · 这台电脑 {Rate(rates.Pc)}");
        // Rebuild the list only when the Mac sent a new one, so open items stay open.
        if (!ReferenceEquals(info.Tips, shownTips))
        {
            shownTips = info.Tips;
            RenderTips(info.Tips ?? []);
            var todo = (info.Tips ?? []).Count(t => t.Status == "act");
            SuggestionBadge.Visibility = todo > 0 ? Visibility.Visible : Visibility.Collapsed;
            SuggestionText.Text = Loc.T(todo == 1 ? "1 suggestion" : $"{todo} suggestions",
                                        todo == 1 ? "1 条建议" : $"{todo} 条建议");
        }
    }

    private void RenderTips(List<TipInfo> tips)
    {
        TipsPanel.Children.Clear();
        foreach (var tip in tips.Where(t => t.Status != "ok")) TipsPanel.Children.Add(TipItem(tip));
        var passed = tips.Where(t => t.Status == "ok").ToList();
        if (passed.Count == 0) return;
        var list = new StackPanel { Spacing = 4 };
        foreach (var tip in passed) list.Children.Add(TipItem(tip));
        TipsPanel.Children.Add(new Expander
        {
            Header = new TextBlock { Text = Loc.T($"{passed.Count} checks passed", $"{passed.Count} 项检查已通过"), Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
            Content = list, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        });
    }

    /// A checklist item: hover for why it matters, open for what Unictrl saw and how to do it.
    private static Expander TipItem(TipInfo tip)
    {
        var resources = Application.Current.Resources;
        var (glyph, brush) = tip.Status switch
        {
            "act" => ("\uE7BA", (Brush)resources["SystemFillColorCautionBrush"]),
            "ok" => ("\uE73E", (Brush)resources["SystemFillColorSuccessBrush"]),
            _ => ("\uE82F", (Brush)resources["TextFillColorSecondaryBrush"]),
        };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = brush, VerticalAlignment = VerticalAlignment.Center });
        var titles = new StackPanel { Spacing = 1 };
        titles.Children.Add(new TextBlock { Text = tip.Title, TextWrapping = TextWrapping.Wrap });
        titles.Children.Add(new TextBlock
        {
            Text = tip.Summary, Style = (Style)resources["CaptionTextBlockStyle"], Foreground = (Brush)resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
        });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        ToolTipService.SetToolTip(header, tip.Summary);

        var body = new StackPanel { Spacing = 10, Padding = new Thickness(28, 2, 0, 4) };
        body.Children.Add(new TextBlock { Text = tip.Summary, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(tip.Seen))
        {
            body.Children.Add(new Border
            {
                Background = (Brush)resources["LayerOnMicaBaseAltFillColorDefaultBrush"], CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock { Text = tip.Seen, TextWrapping = TextWrapping.Wrap, Style = (Style)resources["CaptionTextBlockStyle"] },
            });
        }
        if (!(tip.Steps.Count == 1 && tip.Steps[0] == "Nothing to do."))
        {
            body.Children.Add(new TextBlock
            {
                Text = tip.Status == "ok" ? Loc.T("Already done. For reference:", "已达标。供参考：") : Loc.T("How to", "怎么做"),
                Style = (Style)resources["BodyStrongTextBlockStyle"],
            });
            for (var i = 0; i < tip.Steps.Count; i++)
                body.Children.Add(new TextBlock { Text = $"{i + 1}.  {tip.Steps[i]}", TextWrapping = TextWrapping.Wrap });
        }
        return new Expander
        {
            Header = header, Content = body, IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    // MARK: Pairing

    private void ShowPairing(bool show)
    {
        PairPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PairAgainButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        CancelPairButton.Visibility = string.IsNullOrEmpty(settings.PairingKey) ? Visibility.Collapsed : Visibility.Visible;
        if (show) CodeBox.Focus(FocusState.Programmatic);
    }

    private void OnPairAgain(object sender, RoutedEventArgs e)
    {
        PairStatus.Text = "";
        CodeBox.Text = "";
        ShowPairing(true);
    }

    private void OnCancelPair(object sender, RoutedEventArgs e) => ShowPairing(false);

    private void OnCodeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) OnPairClicked(sender, e);
    }

    private async void OnPairClicked(object sender, RoutedEventArgs e)
    {
        var code = new string(CodeBox.Text.Where(char.IsDigit).ToArray());
        if (code.Length != 6)
        {
            PairStatus.Text = Loc.T("Enter the 6 digits shown on the Mac.", "输入 Mac 上显示的 6 位数字。");
            return;
        }
        PairButton.IsEnabled = false;
        PairStatus.Text = Loc.T("Looking for your Mac…", "正在寻找你的 Mac…");
        var (result, mac) = await Task.Run(() => Pair(code));
        PairButton.IsEnabled = true;
        switch (result.Outcome)
        {
            case PairingClient.Outcome.Paired when mac is not null:
                settings.PairingKey = result.PairingKey!;
                settings.MacId = result.MacId ?? mac.Id;
                settings.MacName = mac.Name;
                settings.MacAddress = mac.Address;
                settings.Port = mac.Port;
                settings.Save();
                Log.Write("paired", $"mac={settings.MacId}");
                // Hand over without a gap: the old session keeps running until the new key's session is up.
                var previous = receiver;
                StopPeer();
                if (settings.Enabled && previous is not null)
                {
                    StartPeer(onConnected: () => previous.Dispose());
                    _ = Task.Delay(10_000).ContinueWith(_ => previous.Dispose());
                }
                else
                {
                    previous?.Dispose();
                    if (settings.Enabled) StartPeer();
                }
                UpdateDetail();
                ShowPairing(false);
                break;
            case PairingClient.Outcome.WrongCode:
                PairStatus.Text = Loc.T("That code didn't match. Check the code on the Mac and try again.", "代码不匹配。请核对 Mac 上显示的代码后重试。");
                break;
            case PairingClient.Outcome.NotPairing:
                PairStatus.Text = Loc.T("Your Mac isn't showing a code. On the Mac, choose Pair a Windows PC… first.", "你的 Mac 没有在显示代码。请先在 Mac 上选择“配对 Windows PC…”。");
                break;
            default:
                PairStatus.Text = Loc.T("No Mac with Unictrl found on this network. Check that both are on the same network.", "在此网络中未发现运行 Unictrl 的 Mac。请确认两台电脑在同一网络。");
                break;
        }
    }

    /// Tries each Mac that answers discovery (and the last known address) until one accepts the code.
    private (PairingClient.Result, Discovery.Mac?) Pair(string code)
    {
        var macs = Discovery.Find(TimeSpan.FromMilliseconds(1500));
        if (!string.IsNullOrWhiteSpace(settings.MacAddress) && macs.All(m => m.Address != settings.MacAddress))
            macs.Add(new Discovery.Mac(settings.MacId, settings.MacName, settings.MacAddress, settings.Port));
        var last = new PairingClient.Result(PairingClient.Outcome.Unreachable);
        foreach (var mac in macs)
        {
            var result = PairingClient.Pair(mac.Address, mac.Port, code, settings.DeviceIdBytes);
            if (result.Outcome is PairingClient.Outcome.Paired or PairingClient.Outcome.WrongCode) return (result, mac);
            if (result.Outcome == PairingClient.Outcome.NotPairing) last = result;
        }
        return (last, null);
    }

    private double GetDpiScale() =>
        Native.GetDpiForMonitor(Native.MonitorFromPoint(new Native.Point(0, 0), 1), 0, out var dpi, out _) == 0
            ? dpi / 96.0 : 1.0;

    // MARK: Screens

    private ArrangementInfo? arrangement;
    private List<(ScreenInfo Screen, CanvasRect Rect)> frames = [];
    private double mmScale = 1;
    private string? draggingId;
    private Point dragStart;
    private FrameworkElement? draggingTile;
    private const double MinOverlapMm = 30;

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RenderScreens();

    /// The screens edge to edge at their physical sizes, each centred at its offset below the Mac.
    private void RenderScreens()
    {
        ScreensCanvas.Children.Clear();
        frames = [];
        var screens = arrangement?.Screens ?? [];
        ScreensPlaceholder.Visibility = screens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var width = ScreensCanvas.ActualWidth;
        var height = ScreensCanvas.ActualHeight;
        if (screens.Count == 0 || width <= 0 || height <= 0) return;

        var x = 0.0;
        var mm = new List<(ScreenInfo, CanvasRect)>();
        foreach (var screen in screens)
        {
            mm.Add((screen, new CanvasRect(x, screen.OffsetMm - screen.HeightMm / 2, screen.WidthMm, screen.HeightMm)));
            x += screen.WidthMm;
        }
        var top = mm.Min(m => m.Item2.Top);
        var bottom = mm.Max(m => m.Item2.Bottom);
        const double padding = 14;
        mmScale = Math.Min((width - 2 * padding) / x, (height - 2 * padding) / Math.Max(bottom - top, 200));
        var originX = (width - x * mmScale) / 2;
        var originY = (height - (bottom - top) * mmScale) / 2 - top * mmScale;
        foreach (var (screen, rect) in mm)
        {
            var r = new CanvasRect(originX + rect.X * mmScale, originY + rect.Y * mmScale, rect.Width * mmScale, rect.Height * mmScale);
            frames.Add((screen, r));
            ScreensCanvas.Children.Add(Tile(screen, r));
        }
    }

    private FrameworkElement Tile(ScreenInfo screen, CanvasRect rect)
    {
        var mine = screen.Id == arrangement?.You;
        var green = mine || screen.Online;
        var resources = Application.Current.Resources;
        Brush foreground = mine ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : screen.Online ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x0B, 0x4A, 0x40)) // pine ink
            : (Brush)resources["TextFillColorPrimaryBrush"];
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        content.Children.Add(new FontIcon { Glyph = screen.IsMac ? "" : "", FontSize = 18, Foreground = foreground, Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock
        {
            Text = screen.Name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = foreground,
            TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = rect.Width - 12,
        });
        content.Children.Add(new TextBlock
        {
            Text = mine ? "This PC" : screen.Online ? $"{screen.WidthMm / 10:F0} × {screen.HeightMm / 10:F0} cm" : "Offline",
            FontSize = 11, Foreground = foreground, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center,
        });
        var tile = new Border
        {
            Width = Math.Max(0, rect.Width - 3), Height = Math.Max(0, rect.Height - 3), CornerRadius = new CornerRadius(6),
            Background = mine ? (Brush)resources["PineGradientBrush"]
                : screen.Online ? (Brush)resources["PineLightGradientBrush"] : (Brush)resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)resources["CardStrokeColorDefaultBrush"], BorderThickness = new Thickness(1),
            Opacity = screen.Online || mine ? 1 : 0.55, Child = content, Tag = screen.Id,
            Shadow = new ThemeShadow(), Translation = new System.Numerics.Vector3(0, 0, 4),
        };
        Canvas.SetLeft(tile, rect.X + 1.5);
        Canvas.SetTop(tile, rect.Y + 1.5);
        if (!screen.IsMac)
        {
            tile.PointerPressed += OnTilePressed;
            tile.PointerMoved += OnTileMoved;
            tile.PointerReleased += OnTileReleased;
            tile.PointerCaptureLost += (_, _) => { if (draggingId is not null) { draggingId = null; RenderScreens(); } };
            tile.PointerEntered += (s, _) => ((UIElement)s).ProtectedCursor(InputSystemCursorShape.SizeAll);
        }
        var rename = new MenuFlyoutItem { Text = "Rename…", Icon = new FontIcon { Glyph = "\uE8AC" } };
        rename.Click += (_, _) => RenameScreen(screen);
        tile.ContextFlyout = new MenuFlyout { Items = { rename } };
        return tile;
    }

    private void OnTilePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement tile || tile.Tag is not string id) return;
        if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return; // right-click opens the menu
        draggingId = id;
        draggingTile = tile;
        dragStart = e.GetCurrentPoint(ScreensCanvas).Position;
        tile.CapturePointer(e.Pointer);
        Canvas.SetZIndex(tile, 1);
        tile.Translation = new System.Numerics.Vector3(0, 0, 24);
        e.Handled = true;
    }

    private void OnTileMoved(object sender, PointerRoutedEventArgs e)
    {
        if (draggingId is null || draggingTile is null) return;
        var frame = frames.First(f => f.Screen.Id == draggingId).Rect;
        var at = e.GetCurrentPoint(ScreensCanvas).Position;
        Canvas.SetLeft(draggingTile, frame.X + 1.5 + at.X - dragStart.X);
        Canvas.SetTop(draggingTile, frame.Y + 1.5 + at.Y - dragStart.Y);
    }

    private void OnTileReleased(object sender, PointerRoutedEventArgs e)
    {
        if (draggingId is null || arrangement is null) return;
        var id = draggingId;
        draggingId = null;
        (sender as UIElement)?.ReleasePointerCaptures();
        var at = e.GetCurrentPoint(ScreensCanvas).Position;
        Drop(id, at.X - dragStart.X, at.Y - dragStart.Y);
    }

    /// Asks for a new name; the Mac saves it and every screen shows it.
    private async void RenameScreen(ScreenInfo screen)
    {
        var box = new TextBox { Text = screen.Name, PlaceholderText = Loc.T("The computer's own name", "电脑自己的名称"), MaxLength = 40 };
        box.SelectAll();
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = screen.IsMac ? Loc.T("Rename your Mac", "重命名你的 Mac")
                : screen.Id == arrangement?.You ? Loc.T("Rename this PC", "重命名这台电脑")
                : Loc.T($"Rename {screen.Name}", $"重命名 {screen.Name}"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    box,
                    new TextBlock
                    {
                        Text = Loc.T("Shown on the Mac and on every PC. Leave empty for the computer's own name.",
                                     "在 Mac 和每台电脑上显示。留空则使用电脑自己的名称。"),
                        TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    },
                },
            },
            PrimaryButtonText = Loc.T("Rename", "重命名"),
            CloseButtonText = Loc.T("Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        receiver?.Rename(screen.Id, box.Text.Trim());
        Log.Write("rename_requested", screen.IsMac ? "mac" : screen.Id[..Math.Min(8, screen.Id.Length)]);
    }

    /// Names follow the Mac's arrangement: the Mac's name in the status and pairing card, this PC's below.
    private void ShowNames()
    {
        if (arrangement is null) return;
        var mac = arrangement.Screens.FirstOrDefault(s => s.IsMac)?.Name;
        if (!string.IsNullOrEmpty(mac) && mac != settings.MacName)
        {
            settings.MacName = mac;
            settings.Save();
        }
        if (connected && !string.IsNullOrEmpty(mac)) StatusText.Text = $"Connected to {mac}";
        var me = arrangement.Screens.FirstOrDefault(s => s.Id == arrangement.You)?.Name ?? Environment.MachineName;
        AboutText.Text = $"Unictrl {typeof(App).Assembly.GetName().Version?.ToString(3)} · This PC: {me}";
        UpdateDetail();
    }

    /// Reorders by where the screen was dropped and keeps it sharing enough edge with its neighbours;
    /// shows the result at once and sends it to the Mac, which answers with what it saved.
    private void Drop(string id, double dx, double dy)
    {
        if (arrangement is null) return;
        var moved = frames.First(f => f.Screen.Id == id);
        var centerX = moved.Rect.X + moved.Rect.Width / 2 + dx;
        var others = frames.Where(f => f.Screen.Id != id).ToList();
        var index = others.FindIndex(f => centerX < f.Rect.X + f.Rect.Width / 2);
        var order = others.Select(f => f.Screen).ToList();
        order.Insert(index < 0 ? order.Count : index, moved.Screen);

        var offset = moved.Screen.OffsetMm + dy / mmScale;
        var at = order.IndexOf(moved.Screen);
        foreach (var neighbor in new[] { at > 0 ? order[at - 1] : null, at < order.Count - 1 ? order[at + 1] : null })
        {
            if (neighbor is null) continue;
            var limit = Math.Max(0, (moved.Screen.HeightMm + neighbor.HeightMm) / 2 - MinOverlapMm);
            offset = Math.Clamp(offset, neighbor.OffsetMm - limit, neighbor.OffsetMm + limit);
            if (Math.Abs(offset - neighbor.OffsetMm) < 6) offset = neighbor.OffsetMm; // snap to centred
        }
        offset = Math.Round(offset, 1);
        order[at] = moved.Screen with { OffsetMm = offset };
        arrangement = arrangement with { Screens = order };
        RenderScreens();
        var offsets = order.Where(s => !s.IsMac).ToDictionary(s => s.Id, s => s.OffsetMm);
        receiver?.EditArrangement(order.Select(s => s.Id), offsets);
        Log.Write("arrangement_edit", string.Join(",", order.Select(s => s.IsMac ? "mac" : s.Id[..Math.Min(8, s.Id.Length)])));
    }
}

internal static class CursorExtensions
{
    /// UIElement.ProtectedCursor is protected; set it by reflection so a plain Border can show the move cursor.
    public static void ProtectedCursor(this UIElement element, InputSystemCursorShape shape) =>
        typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(element, InputSystemCursor.Create(shape));
}
