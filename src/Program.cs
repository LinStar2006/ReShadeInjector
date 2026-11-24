using System;
using System.Windows;

namespace HoYoInjector
{
    internal static class Program
    {
        /// <summary>
        /// 入口。用法：ReShadeInjector.exe [目标进程名] [DLL 路径]
        /// 提权重启时会带着这两个参数回来，因此会自动接续注入。
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            // 不做全局异常兜底的话，XAML 里任何一个笔误都会静默退出
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "injector-error.log"),
                        "[" + DateTime.Now + "] " + (ex != null ? ex.ToString() : e.ExceptionObject.ToString()) + Environment.NewLine);
                }
                catch { }

                try
                {
                    MessageBox.Show(ex != null ? ex.Message : "未知错误", "ReShade Injector",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            };

            try
            {
                // 单实例：先抢锁再建窗口。放在最前面是因为提权重启也会走这条路 ——
                // 新（提权）实例必须能拿到旧实例已经放掉的锁，才能接续注入。
                //
                // 已经在跑时：SingleInstance 自己已经把旧窗口叫到最前面了，
                // 这里直接静默退出，不弹任何提示框 —— 用户双击图的只是「把那个窗口切过来」，
                // 再让他点一次「确定」是多余的。
                string notice;
                if (!SingleInstance.TryTake(args, out notice))
                {
                    return;
                }

                new MainWindow().Run(args);
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n\n" + ex.Message, "ReShade Injector",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}