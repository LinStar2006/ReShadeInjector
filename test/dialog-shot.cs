// 给自绘对话框截图，确认深色样式生效：dialog-shot.exe <输出目录>
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HoYoInjector;

internal static class DialogShot
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);

        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ShotPrompt(Path.Combine(dir, "dialog-prompt.png"));
        ShotConfirm(Path.Combine(dir, "dialog-confirm.png"));
        ShotDanger(Path.Combine(dir, "dialog-delete.png"));

        Console.WriteLine("RESULT: DONE");
        return 0;
    }

    private static void ShotPrompt(string path)
    {
        DispatcherTimer timer = MakeTimer(path);
        timer.Start();
        bool cancelled;
        DialogKit.Prompt(null, "预设名称",
            "会在 Shader\\<当前游戏>\\ 下新建同名文件夹。", "MyPreset", out cancelled);
    }

    private static void ShotConfirm(string path)
    {
        DispatcherTimer timer = MakeTimer(path);
        timer.Start();
        DialogKit.Confirm(null, "应用预设",
            "把「001」的 Shaders / Textures 路径写进该游戏目录下的 ReShade.ini。",
            "应用", false);
    }

    private static void ShotDanger(string path)
    {
        DispatcherTimer timer = MakeTimer(path);
        timer.Start();
        DialogKit.Confirm(null, "删除预设",
            "预设「001」连同里面的 Shaders / Textures 会一起删掉，不能撤销。\n\nC:\\Users\\North\\Documents\\ReShadeInjector\\Shader\\YuanShen\\001",
            "删除", true);
    }

    private static DispatcherTimer MakeTimer(string path)
    {
        var timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(700);
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            Window w = null;
            if (Application.Current.Windows.Count > 0)
                w = Application.Current.Windows[Application.Current.Windows.Count - 1];
            if (w == null) return;

            w.UpdateLayout();
            Save(w, path);
            w.Close();
        };
        return timer;
    }

    private static void Save(Window w, string path)
    {
        int pw = (int)Math.Ceiling(w.ActualWidth);
        int ph = (int)Math.Ceiling(w.ActualHeight);
        if (pw <= 0 || ph <= 0) { pw = 420; ph = 220; }

        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(w);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            encoder.Save(fs);

        Console.WriteLine("saved: " + Path.GetFileName(path) + "  " + pw + "x" + ph);
    }
}