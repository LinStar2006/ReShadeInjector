// 「预设 → ini → 注入 → ReShade 实际加载」整条链路的端到端验证工具。
//
// 用法：preset-e2e.exe <游戏目录>
//
// 它自己所在的目录被当作「程序目录」，会在里面造两个预设：
//   Shader\YuanShen\PresetA\Shaders\ProbeA.fx
//   Shader\YuanShen\PresetB\Shaders\ProbeB.fx
// 然后选中 PresetA 并把路径写进游戏目录的 ReShade.ini。
// 之后由外面的脚本注入 ReShade64.dll，再读 ReShade.log：
// 日志里应该出现 ProbeA.fx 的完整路径、不该出现 ProbeB.fx ——
// 这才是「预设真的生效了」的证据，光看 ini 写得对不对不算。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    internal static class PresetE2E
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            int code = 0;
            try { code = Run(args); }
            catch (Exception ex)
            {
                Console.WriteLine("未处理异常: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                code = 3;
            }
            Console.Out.Flush();
            return code;
        }

        private static int Run(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: preset-e2e.exe <游戏目录> [预设名，默认 PresetA]");
                return 2;
            }

            string wantPreset = args.Length > 1 && args[1].Trim().Length > 0 ? args[1].Trim() : "PresetA";

            string gameDir = Path.GetFullPath(args[0]);
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            Console.WriteLine("程序目录: " + appDir);
            Console.WriteLine("游戏目录: " + gameDir);
            if (!Directory.Exists(gameDir))
            {
                Console.WriteLine("游戏目录不存在。");
                return 1;
            }

            MakeProbe("YuanShen", "PresetA", "ProbeA.fx");
            MakeProbe("YuanShen", "PresetB", "ProbeB.fx");

            PresetManager.SetCurrent("YuanShen", wantPreset);
            Console.WriteLine("当前预设: " + PresetManager.GetCurrent("YuanShen"));
            Console.WriteLine("这次要验的预设: " + wantPreset);

            List<string> report;
            string err;
            bool ok = IniPatcher.Ensure(gameDir, "YuanShen", PresetManager.GetCurrent("YuanShen"),
                                        out report, out err);

            Console.WriteLine("Ensure 返回: " + ok + (string.IsNullOrEmpty(err) ? "" : "  err=" + err));
            if (report != null)
            {
                for (int i = 0; i < report.Count; i++) Console.WriteLine("  - " + report[i]);
            }

            string ini = Path.Combine(gameDir, "ReShade.ini");
            if (!File.Exists(ini))
            {
                Console.WriteLine("ini 没生成，后面没法验。");
                return 1;
            }

            string text = File.ReadAllText(ini, Encoding.UTF8);
            Console.WriteLine();
            Console.WriteLine("EffectSearchPaths  = " + Grab(text, "EffectSearchPaths"));
            Console.WriteLine("TextureSearchPaths = " + Grab(text, "TextureSearchPaths"));

            return ok ? 0 : 1;
        }

        private static void MakeProbe(string gameKey, string presetName, string fxName)
        {
            string dir = PresetManager.PresetDir(gameKey, presetName);
            string shaders = Path.Combine(dir, "Shaders");
            string textures = Path.Combine(dir, "Textures");
            Directory.CreateDirectory(shaders);
            Directory.CreateDirectory(textures);

            // 故意写一个编译不过的 fx：ReShade 会在日志里记下完整路径和失败原因，
            // 正好用来确认它确实扫描了这个预设目录（能编译反而看不出路径）。
            File.WriteAllText(Path.Combine(shaders, fxName),
                "// 探针文件：内容故意不合法，只为让 ReShade 日志里出现本文件路径。\r\n" +
                "this_is_not_valid_hlsl\r\n",
                new UTF8Encoding(false));

            Console.WriteLine("造探针: " + Path.Combine(shaders, fxName));
        }

        private static string Grab(string text, string key)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(key.Length + 1).Trim();
            }
            return null;
        }
    }
}