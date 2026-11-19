using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace XxtWidget
{
    public class LoginDialog : Window
    {
        public string Account { get; private set; }
        public string Password { get; private set; }

        private TextBox _accountBox;
        private PasswordBox _passwordBox;
        private TextBlock _error;
        private Grid _titleBar;

        public LoginDialog(string account)
        {
            Account = account == null ? "" : account;
            Password = "";
            Title = "账号与密码";
            Width = 388;
            Height = 316;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Build();
            KeyDown += Dialog_KeyDown;
            Loaded += delegate
            {
                _accountBox.Focus();
                _accountBox.SelectAll();
            };
        }

        private void Build()
        {
            Border root = new Border();
            root.CornerRadius = new CornerRadius(16);
            root.Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x14, 0x18, 0x22));
            root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
            root.BorderThickness = new Thickness(1);
            root.Padding = new Thickness(22, 18, 22, 18);

            StackPanel panel = new StackPanel();

            _titleBar = new Grid();
            _titleBar.Background = Brushes.Transparent;
            _titleBar.Height = 30;
            TextBlock title = new TextBlock();
            title.Text = "学习通账号";
            title.FontSize = 14;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = new SolidColorBrush(Color.FromArgb(0xF5, 255, 255, 255));
            title.VerticalAlignment = VerticalAlignment.Center;
            _titleBar.Children.Add(title);
            Border close = MakeIconButton("\uE8BB", 11);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Center;
            close.MouseLeftButtonUp += delegate { DialogResult = false; };
            _titleBar.Children.Add(close);
            _titleBar.MouseLeftButtonDown += delegate
            {
                try { DragMove(); } catch { }
            };
            panel.Children.Add(_titleBar);

            panel.Children.Add(MakeLabel("手机号 / 账号", 16));
            _accountBox = new TextBox();
            _accountBox.Text = Account;
            StyleTextBox(_accountBox);
            _accountBox.Height = 34;
            panel.Children.Add(_accountBox);

            panel.Children.Add(MakeLabel("密码", 12));
            _passwordBox = new PasswordBox();
            StylePasswordBox(_passwordBox);
            _passwordBox.Height = 34;
            panel.Children.Add(_passwordBox);

            _error = new TextBlock();
            _error.FontSize = 10;
            _error.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0x9A));
            _error.Height = 18;
            _error.Margin = new Thickness(0, 7, 0, 0);
            panel.Children.Add(_error);

            TextBlock hint = new TextBlock();
            hint.Text = "密码只保存在本机，使用 Windows 凭据加密（DPAPI）";
            hint.FontSize = 9;
            hint.Foreground = new SolidColorBrush(Color.FromArgb(0x59, 255, 255, 255));
            hint.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(hint);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Border cancel = MakeButton("取消", false);
            cancel.MouseLeftButtonUp += delegate { DialogResult = false; };
            Border save = MakeButton("保存", true);
            save.MouseLeftButtonUp += delegate { Save(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            root.Child = panel;
            Content = root;
        }

        private static TextBlock MakeLabel(string text, double topMargin)
        {
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontSize = 10;
            label.Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));
            label.Margin = new Thickness(1, topMargin, 0, 6);
            return label;
        }

        private static void StyleTextBox(TextBox box)
        {
            box.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            box.Foreground = Brushes.White;
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(8, 7, 8, 7);
            box.FontSize = 12;
            box.CaretBrush = Brushes.White;
        }

        private static void StylePasswordBox(PasswordBox box)
        {
            box.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            box.Foreground = Brushes.White;
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(8, 7, 8, 7);
            box.FontSize = 12;
        }

        private static Border MakeIconButton(string glyph, double size)
        {
            Border border = new Border();
            border.Width = 26;
            border.Height = 26;
            border.CornerRadius = new CornerRadius(7);
            border.Background = Brushes.Transparent;
            border.Cursor = Cursors.Hand;
            TextBlock text = new TextBlock();
            text.Text = glyph;
            text.FontFamily = new FontFamily("Segoe MDL2 Assets");
            text.FontSize = size;
            text.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255));
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            border.Child = text;
            return border;
        }

        private static Border MakeButton(string text, bool primary)
        {
            Border border = new Border();
            border.CornerRadius = new CornerRadius(7);
            border.Padding = new Thickness(16, 7, 16, 7);
            border.Margin = new Thickness(8, 0, 0, 0);
            border.Cursor = Cursors.Hand;
            border.Background = new SolidColorBrush(primary ? Color.FromArgb(0xE6, 0x7E, 0xA2, 0xFF) : Color.FromArgb(0x16, 255, 255, 255));
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontSize = 11.5;
            label.Foreground = primary ? new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x15)) : Brushes.White;
            border.Child = label;
            return border;
        }

        private void Dialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Save(); e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        }

        private void Save()
        {
            string account = _accountBox.Text.Trim();
            string password = _passwordBox.Password;
            if (account.Length == 0 || password.Length == 0)
            {
                _error.Text = "请填写账号和密码";
                return;
            }
            Account = account;
            Password = password;
            DialogResult = true;
        }
    }
}
