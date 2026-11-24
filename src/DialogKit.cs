// 程序自绘的确认框 / 输入框。
// 系统 MessageBox 一律是浅色原生窗口（标题栏、白色背景、系统字体），
// 放在这套深色界面里非常突兀，所以这里全部自己画。
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace HoYoInjector
{
    internal static class DialogKit
    {
        private const string ResourceName = "HoYoInjector.dialogs.xaml";

        private static ResourceDictionary _resources;

        private static ResourceDictionary Res
        {
            get
            {
                if (_resources == null) _resources = Load();
                return _resources;
            }
        }

        private static ResourceDictionary Load()
        {
            using (Stream stream = typeof(DialogKit).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("找不到嵌入资源：" + ResourceName);

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return (ResourceDictionary)XamlReader.Parse(reader.ReadToEnd());
            }
        }

        private static Style StyleOf(string key)
        {
            return (Style)Res[key];
        }

        private static Brush BrushOf(string key)
        {
            return (Brush)Res[key];
        }

        /// <summary>
        /// 画对话框的壳：无标题栏的圆角深色面板。
        /// 返回窗口本身，panel 是往里面塞内容的容器。
        /// </summary>
        private static Window CreateShell(Window owner, string title, double width, out StackPanel panel)
        {
            var win = new Window();
            if (owner != null) win.Owner = owner;
            win.WindowStyle = WindowStyle.None;
            win.AllowsTransparency = true;
            win.Background = Brushes.Transparent;
            win.ResizeMode = ResizeMode.NoResize;
            win.SizeToContent = SizeToContent.WidthAndHeight;
            win.WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            win.ShowInTaskbar = false;
            win.Resources = Res;

            var shell = new Border();
            shell.Style = StyleOf("DlgPanel");

            panel = new StackPanel();
            panel.Margin = new Thickness(18);
            panel.Width = width;

            var caption = new TextBlock();
            caption.Text = title;
            caption.FontSize = 13;
            caption.FontWeight = FontWeights.SemiBold;
            caption.Foreground = BrushOf("DlgTitle");
            panel.Children.Add(caption);

            shell.Child = panel;
            win.Content = shell;

            // 没有标题栏，Esc 取消得自己接
            win.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) win.DialogResult = false;
            };

            return win;
        }

        /// <summary>
        /// 确认框。danger 为 true 时确定按钮是红色 —— 用在删除这类不可撤销的操作上。
        /// </summary>
        public static bool Confirm(Window owner, string title, string message, string okText, bool danger)
        {
            StackPanel panel;
            Window win = CreateShell(owner, title, 360, out panel);

            var text = new TextBlock();
            text.Text = message;
            text.Margin = new Thickness(0, 10, 0, 0);
            text.FontSize = 12;
            text.LineHeight = 19;
            text.TextWrapping = TextWrapping.Wrap;
            text.Foreground = BrushOf("DlgText");
            panel.Children.Add(text);

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, 18, 0, 0);

            var cancel = new Button();
            cancel.Content = "取消";
            cancel.Style = StyleOf("DlgButton");
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += (s, e) => { win.DialogResult = false; };
            row.Children.Add(cancel);

            var ok = new Button();
            ok.Content = okText;
            ok.Style = StyleOf(danger ? "DlgDanger" : "DlgPrimary");
            ok.IsDefault = true;
            ok.Click += (s, e) => { win.DialogResult = true; };
            row.Children.Add(ok);

            panel.Children.Add(row);

            return win.ShowDialog() == true;
        }

        /// <summary>
        /// 单行输入框。返回 null 表示用户取消，cancelled 同步为 true。
        /// </summary>
        public static string Prompt(Window owner, string title, string hint, string initial, out bool cancelled)
        {
            cancelled = false;

            StackPanel panel;
            Window win = CreateShell(owner, title, 320, out panel);

            if (!string.IsNullOrEmpty(hint))
            {
                var tip = new TextBlock();
                tip.Text = hint;
                tip.Margin = new Thickness(0, 8, 0, 0);
                tip.FontSize = 11.5;
                tip.LineHeight = 17;
                tip.TextWrapping = TextWrapping.Wrap;
                tip.Foreground = BrushOf("DlgText");
                panel.Children.Add(tip);
            }

            var box = new TextBox();
            box.Style = StyleOf("DlgInput");
            box.Text = initial == null ? string.Empty : initial;
            box.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(box);

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, 16, 0, 0);

            var cancel = new Button();
            cancel.Content = "取消";
            cancel.Style = StyleOf("DlgButton");
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += (s, e) => { win.DialogResult = false; };
            row.Children.Add(cancel);

            var ok = new Button();
            ok.Content = "确定";
            ok.Style = StyleOf("DlgPrimary");
            ok.IsDefault = true;
            ok.Click += (s, e) => { win.DialogResult = true; };
            row.Children.Add(ok);

            panel.Children.Add(row);

            win.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };

            bool? answer = win.ShowDialog();
            if (answer != true)
            {
                cancelled = true;
                return null;
            }
            return box.Text;
        }
    }
}