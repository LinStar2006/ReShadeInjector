using System;
using System.IO;
using System.Reflection;
using System.Windows.Markup;

namespace HoYoInjector
{
    /// <summary>
    /// 界面定义放在独立的 src/ui.xaml，由 build.ps1 用 /resource 嵌进 exe。
    /// 运行时从资源流解析，因此改界面样式不需要动 C# 代码，
    /// 也避免在 C# 字符串里手写 XAML 转义（那里极易漏引号）。
    /// </summary>
    internal static class UiXaml
    {
        private const string ResourceName = "HoYoInjector.ui.xaml";

        private static string _text;

        /// <summary>解析 XAML 并返回窗口对象（每次调用都是新实例）。</summary>
        public static object LoadWindow()
        {
            return XamlReader.Parse(ReadXamlText());
        }

        /// <summary>读出嵌入的 XAML 文本（便于排错时打印）。</summary>
        public static string ReadXamlText()
        {
            if (_text != null) return _text;

            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(ResourceName))
            {
                if (s == null)
                {
                    var names = string.Join(", ", asm.GetManifestResourceNames());
                    throw new InvalidOperationException(
                        "找不到嵌入资源 " + ResourceName + "。实际资源：[" + names + "]");
                }
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                {
                    _text = r.ReadToEnd();
                }
            }
            return _text;
        }
    }
}