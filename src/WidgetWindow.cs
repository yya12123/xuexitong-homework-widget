using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace XxtWidget
{
    public class WidgetWindow : Window
    {
        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        private readonly string _dir;
        private readonly string _configPath;
        private readonly string _cachePath;
        private readonly AppConfig _config;
        private Grid _ui;
        private ObservableCollection<HomeworkItem> _items = new ObservableCollection<HomeworkItem>();
        private List<HomeworkItem> _all = new List<HomeworkItem>();
        private XxtApi _api;
        private CancellationTokenSource _cts;
        private DispatcherTimer _timer;
        private DispatcherTimer _zTimer;
        private DateTime _nextRefresh;
        private DateTime? _lastSync;
        private List<string> _warnings = new List<string>();
        private string _lastError;
        private bool _syncing;
        private bool _refreshPending;
        private bool _closing;
        private int _tickCount;
        private RotateTransform _refreshRotate;

        private TextBlock _subtitle;
        private Border _btnRefresh;
        private Border _btnMenu;
        private Border _btnClose;
        private Border _errorBar;
        private TextBlock _errorText;
        private Border _btnRetry;
        private ScrollViewer _scroll;
        private ItemsControl _list;
        private StackPanel _emptyPanel;
        private TextBlock _emptyTitle;
        private TextBlock _emptyHint;
        private TextBlock _footerLeft;
        private TextBlock _footerRight;
        private Border _menuPanel;
        private Border _menuClose;
        private Border _chip30;
        private Border _chip60;
        private Border _chip120;
        private Border _toggleOverdue;
        private StackPanel _overdueRangePanel;
        private Border _chip7d;
        private Border _chip30d;
        private Border _toggleAutoStart;
        private Border _btnAccount;
        private TextBlock _accountLabel;
        private Border _btnRefreshNow;
        private Border _btnQuit;
        private Border _card;
        private StackPanel _headerDrag;

        private bool _resizing;
        private ResizeEdge _resizeEdge;
        private Point _resizeStartScreen;
        private Rect _resizeStartRect;
        private bool _dragging;
        private Point _dragStartScreen;
        private double _dragStartLeft;
        private double _dragStartTop;
        private IntPtr _desktopHwnd;

        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public WidgetWindow(string dir, AppConfig config)
        {
            _dir = dir;
            _configPath = Path.Combine(dir, "config.json");
            _cachePath = Path.Combine(dir, "cache.json");
            _config = config;

            Title = "学习通作业";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 340;
            MinHeight = 260;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;

            BuildUi();
            LoadCachedData();
            ApplyInitialBounds();

            if (_config.AutoStart) AutoStart.Set(true);
            else if (AutoStart.IsEnabled()) AutoStart.Set(false);

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _zTimer = new DispatcherTimer();
            _zTimer.Interval = TimeSpan.FromMilliseconds(700);
            _zTimer.Tick += delegate { SendToDesktopLayer(); };
            _nextRefresh = DateTime.Now.AddMinutes(_config.RefreshMinutes);

            Loaded += Window_Loaded;
            Closing += Window_Closing;
            SourceInitialized += Window_SourceInitialized;
        }

        private void BuildUi()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream("ui.xaml"))
            {
                if (stream == null) throw new InvalidOperationException("找不到界面资源 ui.xaml");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    _ui = (Grid)XamlReader.Parse(reader.ReadToEnd());
                }
            }
            Content = _ui;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            _subtitle = Find<TextBlock>("Subtitle");
            _headerDrag = Find<StackPanel>("HeaderDrag");
            _btnRefresh = Find<Border>("BtnRefresh");
            _btnMenu = Find<Border>("BtnMenu");
            _btnClose = Find<Border>("BtnClose");
            _errorBar = Find<Border>("ErrorBar");
            _errorText = Find<TextBlock>("ErrorText");
            _btnRetry = Find<Border>("BtnRetry");
            _scroll = Find<ScrollViewer>("Scroll");
            _list = Find<ItemsControl>("List");
            _emptyPanel = Find<StackPanel>("EmptyPanel");
            _emptyTitle = Find<TextBlock>("EmptyTitle");
            _emptyHint = Find<TextBlock>("EmptyHint");
            _footerLeft = Find<TextBlock>("FooterLeft");
            _footerRight = Find<TextBlock>("FooterRight");
            _menuPanel = Find<Border>("MenuPanel");
            _menuClose = Find<Border>("MenuClose");
            _chip30 = Find<Border>("Chip30");
            _chip60 = Find<Border>("Chip60");
            _chip120 = Find<Border>("Chip120");
            _toggleOverdue = Find<Border>("ToggleOverdue");
            _overdueRangePanel = Find<StackPanel>("OverdueRangePanel");
            _chip7d = Find<Border>("Chip7d");
            _chip30d = Find<Border>("Chip30d");
            _toggleAutoStart = Find<Border>("ToggleAutoStart");
            _btnAccount = Find<Border>("BtnAccount");
            _accountLabel = Find<TextBlock>("AccountLabel");
            _btnRefreshNow = Find<Border>("BtnRefreshNow");
            _btnQuit = Find<Border>("BtnQuit");
            _card = Find<Border>("Card");

            _list.ItemsSource = _items;

            _btnRefresh.MouseLeftButtonUp += delegate { RefreshAsync(true); };
            _btnMenu.MouseLeftButtonUp += delegate { ToggleMenu(); };
            _btnClose.MouseLeftButtonUp += delegate { Close(); };
            _btnRetry.MouseLeftButtonUp += delegate { RefreshAsync(true); };
            _headerDrag.MouseLeftButtonDown += HeaderDrag_MouseLeftButtonDown;
            _card.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e) { ToggleMenu(); e.Handled = true; };
            _list.PreviewMouseLeftButtonUp += List_PreviewMouseLeftButtonUp;

            _menuClose.MouseLeftButtonUp += delegate { HideMenu(); };
            _chip30.MouseLeftButtonUp += delegate { SetRefreshInterval(30); };
            _chip60.MouseLeftButtonUp += delegate { SetRefreshInterval(60); };
            _chip120.MouseLeftButtonUp += delegate { SetRefreshInterval(120); };
            _toggleOverdue.MouseLeftButtonUp += delegate { ToggleOverdue(); };
            _chip7d.MouseLeftButtonUp += delegate { SetOverdueDays(7); };
            _chip30d.MouseLeftButtonUp += delegate { SetOverdueDays(30); };
            _toggleAutoStart.MouseLeftButtonUp += delegate { ToggleAutoStart(); };
            _btnAccount.MouseLeftButtonUp += delegate { HideMenu(); ShowAccountDialog(false); };
            _btnRefreshNow.MouseLeftButtonUp += delegate { HideMenu(); RefreshAsync(true); };
            _btnQuit.MouseLeftButtonUp += delegate { Close(); };

            PreviewMouseMove += Window_PreviewMouseMove;
            PreviewMouseLeftButtonDown += Window_PreviewMouseDown;
            PreviewMouseLeftButtonUp += Window_PreviewMouseUp;

            UpdateMenuState();
            UpdateChrome();
        }

        private T Find<T>(string name) where T : class
        {
            object found = _ui.FindName(name);
            if (found == null) found = LogicalTreeHelper.FindLogicalNode(_ui, name);
            return found as T;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _desktopHwnd = FindDesktopWindow();
            Log.Write("desktop hwnd=" + _desktopHwnd);
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null) source.AddHook(WndProc);
            SendToDesktopLayer();
        }

        private static IntPtr FindDesktopWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr lparam)
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() == "Progman")
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) found = FindWindow("Progman", null);
            return found;
        }

        private static IntPtr FindTopDesktopLayer()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr lparam)
            {
                if (!IsWindowVisible(hwnd)) return true;
                StringBuilder cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string name = cls.ToString();
                if (name == "Progman" || name == "WorkerW")
                {
                    RECT rect;
                    GetWindowRect(hwnd, out rect);
                    if (name == "Progman" || (rect.Right - rect.Left) >= 1000)
                    {
                        found = hwnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private void SendToDesktopLayer()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, 4);
            IntPtr layer = FindTopDesktopLayer();
            IntPtr insertAfter = IntPtr.Zero;
            if (layer != IntPtr.Zero)
            {
                IntPtr above = GetWindow(layer, 3);
                if (above != IntPtr.Zero)
                {
                    int style = GetWindowLong(above, -20);
                    if ((style & 0x8) == 0) insertAfter = above;
                }
            }
            SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(3);
            }
            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SendToDesktopLayer();
            _timer.Start();
            _zTimer.Start();
            if (string.IsNullOrEmpty(_config.Account) || string.IsNullOrEmpty(ConfigStore.GetPassword(_config)))
            {
                ShowAccountDialog(true);
            }
            else
            {
                RefreshAsync(false);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _closing = true;
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try { _timer.Stop(); } catch { }
            try { _zTimer.Stop(); } catch { }
            SaveBounds();
            ConfigStore.Save(_configPath, _config);
        }

        private void LoadCachedData()
        {
            CacheFile cache = CacheStore.Load(_cachePath);
            if (cache == null || cache.Items == null || cache.Items.Count == 0) return;
            List<HomeworkItem> list = new List<HomeworkItem>();
            foreach (CacheItem c in cache.Items)
            {
                HomeworkItem item = new HomeworkItem();
                item.Title = c.Title;
                item.CourseName = c.CourseName;
                item.ClassName = c.ClassName;
                item.DetailUrl = c.DetailUrl;
                item.Status = c.Status;
                item.Deadline = c.DeadlineTicks > 0 ? new DateTime(c.DeadlineTicks) : (DateTime?)null;
                item.PublishDate = c.PublishTicks > 0 ? new DateTime(c.PublishTicks) : (DateTime?)null;
                item.Key = c.DetailUrl;
                list.Add(item);
            }
            DateTime parsed;
            if (DateTime.TryParse(cache.LastSync, out parsed)) _lastSync = parsed;
            _all = list;
            ApplyItems();
        }

        private void ApplyItems()
        {
            _all.Sort(CompareItems);
            for (int i = 0; i < _all.Count; i++) _all[i].Rank = i + 1;
            _items.Clear();
            foreach (HomeworkItem item in _all) _items.Add(item);
            UpdateChrome();
        }

        private static int CompareItems(HomeworkItem a, HomeworkItem b)
        {
            int cmp = a.HoursLeft.CompareTo(b.HoursLeft);
            if (cmp != 0) return cmp;
            return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
        }

        private void UpdateChrome()
        {
            if (_subtitle == null) return;
            int count = _all.Count;
            if (count == 0)
            {
                _emptyPanel.Visibility = Visibility.Visible;
                _scroll.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(_lastError))
                {
                    _emptyTitle.Text = "暂时无法获取作业";
                    _emptyHint.Text = "检查网络后点右上角刷新";
                }
                else
                {
                    _emptyTitle.Text = "暂无待完成作业";
                    _emptyHint.Text = _lastSync.HasValue ? "上次更新 " + _lastSync.Value.ToString("HH:mm") : "正在同步...";
                }
                if (!_syncing && _lastSync.HasValue && string.IsNullOrEmpty(_lastError)) _subtitle.Text = "全部完成 · 暂无待交作业";
                else if (!_syncing && !string.IsNullOrEmpty(_lastError)) _subtitle.Text = "同步失败";
            }
            else
            {
                _emptyPanel.Visibility = Visibility.Collapsed;
                _scroll.Visibility = Visibility.Visible;
                int overdue = 0;
                foreach (HomeworkItem item in _all)
                {
                    if (item.Deadline.HasValue && item.HoursLeft <= 0) overdue++;
                }
                HomeworkItem nearest = null;
                foreach (HomeworkItem item in _all)
                {
                    if (item.Deadline.HasValue && item.HoursLeft > 0) { nearest = item; break; }
                }
                if (overdue > 0)
                {
                    if (count - overdue == 0) _subtitle.Text = string.Format("共 {0} 项 · 均已截止", count);
                    else _subtitle.Text = string.Format("剩 {0} 项 · {1} 项已截止", count - overdue, overdue);
                }
                else if (nearest != null) _subtitle.Text = string.Format("剩 {0} 项 · 最近 {1}", count, nearest.TimeText);
                else _subtitle.Text = string.Format("剩 {0} 项 · 均无截止时间", count);
            }
            _footerLeft.Text = _lastSync.HasValue
                ? "更新于 " + _lastSync.Value.ToString("HH:mm") + (_warnings.Count > 0 ? " · 部分课程失败" : "")
                : "尚未同步";
            if (!_syncing)
            {
                TimeSpan left = _nextRefresh - DateTime.Now;
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                _footerRight.Text = string.Format("{0:00}:{1:00} 后刷新", (int)left.TotalMinutes, left.Seconds);
            }
        }

        private async void RefreshAsync(bool userInitiated)
        {
            if (_syncing)
            {
                _refreshPending = true;
                return;
            }
            string password = ConfigStore.GetPassword(_config);
            if (string.IsNullOrEmpty(_config.Account) || string.IsNullOrEmpty(password))
            {
                ShowError("还没有配置学习通账号，点击右上角菜单中的「账号与密码」设置");
                return;
            }
            _syncing = true;
            _refreshPending = false;
            _lastError = null;
            _errorBar.Visibility = Visibility.Collapsed;
            _btnRefresh.Opacity = 0.45;
            StartRefreshAnimation();
            if (_cts != null) { try { _cts.Cancel(); } catch { } }
            _cts = new CancellationTokenSource();
            try
            {
                if (_api == null) _api = new XxtApi(_config.Account, password);
                Progress<string> progress = new Progress<string>(delegate(string message)
                {
                    if (!_closing) _subtitle.Text = message;
                });
                List<HomeworkItem> list = await _api.FetchAllAsync(_config.IncludeOverdue, _config.OverdueDays, progress, _cts.Token);
                if (_closing) return;
                _all = list;
                _warnings = new List<string>(_api.LastWarnings);
                _lastSync = DateTime.Now;
                ApplyItems();
                CacheStore.Save(_cachePath, _all, _lastSync.Value);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _lastError = Unwrap(ex);
                Log.Write("refresh failed: " + ex);
                ShowError(_lastError);
            }
            finally
            {
                _syncing = false;
                _btnRefresh.Opacity = 1.0;
                StopRefreshAnimation();
                _nextRefresh = DateTime.Now.AddMinutes(_config.RefreshMinutes);
                if (!_closing)
                {
                    UpdateChrome();
                    if (_refreshPending)
                    {
                        _refreshPending = false;
                        RefreshAsync(false);
                    }
                }
            }
        }

        private static string Unwrap(Exception ex)
        {
            AggregateException aggregate = ex as AggregateException;
            if (aggregate != null && aggregate.InnerExceptions.Count > 0) return aggregate.InnerExceptions[0].Message;
            return ex.Message;
        }

        private void ShowError(string message)
        {
            _lastError = message;
            _errorText.Text = message;
            _errorBar.Visibility = Visibility.Visible;
            if (_all.Count == 0) _subtitle.Text = "同步失败";
            Log.Write("error shown: " + message);
        }

        private void StartRefreshAnimation()
        {
            TextBlock glyph = _btnRefresh.Child as TextBlock;
            if (glyph == null) return;
            if (_refreshRotate == null)
            {
                _refreshRotate = new RotateTransform(0);
                glyph.RenderTransform = _refreshRotate;
                glyph.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            DoubleAnimation animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1));
            animation.RepeatBehavior = RepeatBehavior.Forever;
            _refreshRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
        }

        private void StopRefreshAnimation()
        {
            if (_refreshRotate != null) _refreshRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _tickCount++;
            if (_syncing)
            {
                _footerRight.Text = "同步中...";
            }
            else
            {
                TimeSpan left = _nextRefresh - DateTime.Now;
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                _footerRight.Text = string.Format("{0:00}:{1:00} 后刷新", (int)left.TotalMinutes, left.Seconds);
                if (DateTime.Now >= _nextRefresh) RefreshAsync(false);
            }
            if (_tickCount % 30 == 0 && !_syncing && _all.Count > 0)
            {
                foreach (HomeworkItem item in _all) item.RefreshComputed();
                UpdateChrome();
            }
        }

        private void ToggleMenu()
        {
            if (_menuPanel.Visibility == Visibility.Visible) HideMenu();
            else
            {
                UpdateMenuState();
                _menuPanel.Visibility = Visibility.Visible;
            }
        }

        private void HideMenu()
        {
            _menuPanel.Visibility = Visibility.Collapsed;
        }

        private void UpdateMenuState()
        {
            StyleChip(_chip30, _config.RefreshMinutes == 30);
            StyleChip(_chip60, _config.RefreshMinutes == 60);
            StyleChip(_chip120, _config.RefreshMinutes == 120);
            StyleToggle(_toggleOverdue, _config.IncludeOverdue);
            _overdueRangePanel.Visibility = _config.IncludeOverdue ? Visibility.Visible : Visibility.Collapsed;
            StyleChip(_chip7d, _config.OverdueDays == 7);
            StyleChip(_chip30d, _config.OverdueDays == 30);
            StyleToggle(_toggleAutoStart, AutoStart.IsEnabled());
            if (string.IsNullOrEmpty(_config.Account)) _accountLabel.Text = "未配置";
            else if (_config.Account.Length > 7) _accountLabel.Text = _config.Account.Substring(0, 3) + "****" + _config.Account.Substring(_config.Account.Length - 4);
            else _accountLabel.Text = _config.Account;
        }

        private static void StyleChip(Border chip, bool selected)
        {
            chip.Background = new SolidColorBrush(selected ? Color.FromArgb(0x26, 255, 255, 255) : Colors.Transparent);
            chip.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(0x55, 255, 255, 255) : Color.FromArgb(0x1A, 255, 255, 255));
        }

        private static void StyleToggle(Border track, bool on)
        {
            track.Background = new SolidColorBrush(on ? Color.FromArgb(0xB3, 0x7E, 0xA2, 0xFF) : Color.FromArgb(0x24, 255, 255, 255));
            System.Windows.Shapes.Ellipse knob = track.Child as System.Windows.Shapes.Ellipse;
            if (knob == null) return;
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            knob.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
        }

        private void SetOverdueDays(int days)
        {
            _config.OverdueDays = days;
            ConfigStore.Save(_configPath, _config);
            UpdateMenuState();
            if (_config.IncludeOverdue)
            {
                HideMenu();
                RefreshAsync(true);
            }
        }

        private void SetRefreshInterval(int minutes)
        {
            _config.RefreshMinutes = minutes;
            ConfigStore.Save(_configPath, _config);
            _nextRefresh = DateTime.Now.AddMinutes(minutes);
            UpdateMenuState();
            UpdateChrome();
        }

        private void ToggleOverdue()
        {
            _config.IncludeOverdue = !_config.IncludeOverdue;
            ConfigStore.Save(_configPath, _config);
            UpdateMenuState();
            HideMenu();
            RefreshAsync(true);
        }

        private void ToggleAutoStart()
        {
            bool target = !AutoStart.IsEnabled();
            bool ok = AutoStart.Set(target);
            if (ok)
            {
                _config.AutoStart = target;
                ConfigStore.Save(_configPath, _config);
            }
            else
            {
                ShowError("设置开机自启失败，请检查系统权限");
            }
            UpdateMenuState();
        }

        private void ShowAccountDialog(bool required)
        {
            LoginDialog dialog = new LoginDialog(_config.Account);
            dialog.Owner = this;
            bool? result = dialog.ShowDialog();
            if (result == true && !string.IsNullOrEmpty(dialog.Account) && !string.IsNullOrEmpty(dialog.Password))
            {
                _config.Account = dialog.Account.Trim();
                ConfigStore.SetPassword(_config, dialog.Password);
                ConfigStore.Save(_configPath, _config);
                if (_api != null) { _api.Dispose(); _api = null; }
                _lastError = null;
                _errorBar.Visibility = Visibility.Collapsed;
                UpdateMenuState();
                RefreshAsync(true);
            }
            else if (required)
            {
                ShowError("还没有配置学习通账号，点击右上角菜单中的「账号与密码」设置");
            }
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            ContentPresenter presenter = FindAncestor<ContentPresenter>(source);
            if (presenter == null) return;
            HomeworkItem item = presenter.DataContext as HomeworkItem;
            if (item == null || string.IsNullOrEmpty(item.DetailUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(item.DetailUrl) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("open url: " + ex.Message); }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                T typed = current as T;
                if (typed != null) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void HeaderDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            _dragging = true;
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            _dragStartLeft = Left;
            _dragStartTop = Top;
            Mouse.Capture(this);
            e.Handled = true;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_menuPanel.Visibility == Visibility.Visible) return;
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox) return;
            ResizeEdge edge = HitTestResize(e.GetPosition(this));
            if (edge == ResizeEdge.None) return;
            _resizing = true;
            _resizeEdge = edge;
            _resizeStartScreen = PointToScreen(e.GetPosition(this));
            _resizeStartRect = new Rect(Left, Top, Width, Height);
            Mouse.Capture(this);
            e.Handled = true;
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                Point currentDrag = PointToScreen(e.GetPosition(this));
                DpiScale dragDpi = VisualTreeHelper.GetDpi(this);
                Left = _dragStartLeft + (currentDrag.X - _dragStartScreen.X) / dragDpi.DpiScaleX;
                Top = _dragStartTop + (currentDrag.Y - _dragStartScreen.Y) / dragDpi.DpiScaleY;
                e.Handled = true;
                return;
            }
            if (_resizing)
            {
                Point current = PointToScreen(e.GetPosition(this));
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double dx = (current.X - _resizeStartScreen.X) / dpi.DpiScaleX;
                double dy = (current.Y - _resizeStartScreen.Y) / dpi.DpiScaleY;
                double left = _resizeStartRect.Left;
                double top = _resizeStartRect.Top;
                double width = _resizeStartRect.Width;
                double height = _resizeStartRect.Height;
                if (_resizeEdge == ResizeEdge.Left || _resizeEdge == ResizeEdge.TopLeft || _resizeEdge == ResizeEdge.BottomLeft)
                {
                    left += dx;
                    width -= dx;
                }
                if (_resizeEdge == ResizeEdge.Right || _resizeEdge == ResizeEdge.TopRight || _resizeEdge == ResizeEdge.BottomRight)
                {
                    width += dx;
                }
                if (_resizeEdge == ResizeEdge.Top || _resizeEdge == ResizeEdge.TopLeft || _resizeEdge == ResizeEdge.TopRight)
                {
                    top += dy;
                    height -= dy;
                }
                if (_resizeEdge == ResizeEdge.Bottom || _resizeEdge == ResizeEdge.BottomLeft || _resizeEdge == ResizeEdge.BottomRight)
                {
                    height += dy;
                }
                if (width < MinWidth)
                {
                    if (_resizeEdge == ResizeEdge.Left || _resizeEdge == ResizeEdge.TopLeft || _resizeEdge == ResizeEdge.BottomLeft)
                    {
                        left = _resizeStartRect.Right - MinWidth;
                    }
                    width = MinWidth;
                }
                if (height < MinHeight)
                {
                    if (_resizeEdge == ResizeEdge.Top || _resizeEdge == ResizeEdge.TopLeft || _resizeEdge == ResizeEdge.TopRight)
                    {
                        top = _resizeStartRect.Bottom - MinHeight;
                    }
                    height = MinHeight;
                }
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                e.Handled = true;
                return;
            }
            ResizeEdge hover = HitTestResize(e.GetPosition(this));
            Cursor = CursorFor(hover);
        }

        private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Mouse.Capture(null);
                Cursor = null;
                SaveBounds();
                e.Handled = true;
                return;
            }
            if (!_resizing) return;
            _resizing = false;
            _resizeEdge = ResizeEdge.None;
            Mouse.Capture(null);
            Cursor = null;
            SaveBounds();
            e.Handled = true;
        }

        private ResizeEdge HitTestResize(Point p)
        {
            const double inset = 12;
            const double band = 7;
            double w = ActualWidth;
            double h = ActualHeight;
            bool left = p.X >= inset - band && p.X <= inset + band;
            bool right = p.X >= w - inset - band && p.X <= w - inset + band;
            bool top = p.Y >= inset - band && p.Y <= inset + band;
            bool bottom = p.Y >= h - inset - band && p.Y <= h - inset + band;
            if (left && top) return ResizeEdge.TopLeft;
            if (right && top) return ResizeEdge.TopRight;
            if (left && bottom) return ResizeEdge.BottomLeft;
            if (right && bottom) return ResizeEdge.BottomRight;
            if (left) return ResizeEdge.Left;
            if (right) return ResizeEdge.Right;
            if (top) return ResizeEdge.Top;
            if (bottom) return ResizeEdge.Bottom;
            return ResizeEdge.None;
        }

        private static Cursor CursorFor(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.Right:
                    return Cursors.SizeWE;
                case ResizeEdge.Top:
                case ResizeEdge.Bottom:
                    return Cursors.SizeNS;
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomLeft:
                    return Cursors.SizeNESW;
                default:
                    return null;
            }
        }

        private void ApplyInitialBounds()
        {
            double width = _config.WindowWidth > 0 ? _config.WindowWidth : 440;
            double height = _config.WindowHeight > 0 ? _config.WindowHeight : 590;
            if (width < MinWidth) width = MinWidth;
            if (height < MinHeight) height = MinHeight;
            Width = width;
            Height = height;
            bool hasSaved = _config.WindowLeft >= 0 && _config.WindowTop >= 0;
            if (hasSaved)
            {
                Left = _config.WindowLeft;
                Top = _config.WindowTop;
            }
            if (!hasSaved || !IsMostlyOnScreen())
            {
                PlaceBottomRight();
            }
            else
            {
                Rect work = SystemParameters.WorkArea;
                double centerX = Left + Width / 2;
                if (centerX >= work.Left && centerX <= work.Right)
                {
                    if (Left < work.Left + 10) Left = work.Left + 10;
                    if (Left + Width > work.Right - 10) Left = work.Right - Width - 10;
                    if (Top < work.Top + 10) Top = work.Top + 10;
                    if (Top + Height > work.Bottom - 10) Top = work.Bottom - Height - 10;
                }
            }
        }

        private bool IsMostlyOnScreen()
        {
            Rect window = new Rect(Left, Top, Width, Height);
            Rect virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Rect overlap = Rect.Intersect(window, virtualScreen);
            return !overlap.IsEmpty && overlap.Width >= 120 && overlap.Height >= 80;
        }

        private void PlaceBottomRight()
        {
            Rect work = SystemParameters.WorkArea;
            Left = Math.Max(work.Left + 10, work.Right - Width - 20);
            Top = Math.Max(work.Top + 10, work.Bottom - Height - 20);
        }

        private void SaveBounds()
        {
            if (_config == null) return;
            if (WindowState != WindowState.Normal) return;
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
            ConfigStore.Save(_configPath, _config);
        }
    }
}
