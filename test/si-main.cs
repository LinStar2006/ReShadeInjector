using System;

namespace HoYoInjector
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int code = SingleInstanceTest.Execute(args);
            Environment.ExitCode = code;
        }
    }
}