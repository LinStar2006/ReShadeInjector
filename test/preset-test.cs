// 预设管理 + zip 解压的自检程序。
//
// 跑法：preset-test.exe <测试根目录>
// 测试根目录必须放在工作区里，不能用 %TEMP%。
// （沙箱对 pwsh 派生的 .NET 子进程写 %TEMP% 有限制，会报 UnauthorizedAccessException）
//
// 覆盖：EnsureDefault / Create / List / ImportFromFolder（含嵌套 reshade-shaders）/
//        GetCurrent+SetCurrent 往返 / Resolve / ZipFile 解压可用性。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    internal static class PresetTest
    {
        private static int _fail;

        private static void Report(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail == null ? "" : "  ->  " + detail));
            if (!ok) _fail++;
        }

        /// <summary>从 ini 文本里取 key=value 的 value，取不到返回 null。</summary>
        private static string GrabValue(string text, string key)
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

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            int code = 0;
            try { code = Execute(args); }
            catch (Exception ex)
            {
                Console.WriteLine("未处理异常: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                code = 3;
            }
            Console.Out.Flush();
            return code;
        }

        private static int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: preset-test.exe <测试根目录>");
                return 2;
            }

            string root = Path.GetFullPath(args[0]);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);

            // 清场：本程序目录就是 PresetManager 的 AppDir，上次跑留下的
            // Shader\ 和 settings.ini 会让「创建成功」「默认是 Default」这些断言误判。
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            try { if (Directory.Exists(Path.Combine(appDir, "Shader"))) Directory.Delete(Path.Combine(appDir, "Shader"), true); }
            catch { }
            try { if (File.Exists(Path.Combine(appDir, "settings.ini"))) File.Delete(Path.Combine(appDir, "settings.ini")); }
            catch { }

            Console.WriteLine("程序目录: " + appDir);
            Console.WriteLine("测试根目录: " + root);
            Console.WriteLine();

            // ---- A：EnsureDefault ----
            Console.WriteLine("A. EnsureDefault 建 Default 预设");
            PresetManager.EnsureDefault("YuanShen");
            string dflt = PresetManager.PresetDir("YuanShen", PresetManager.DefaultName);
            Report("Default\\Shaders 存在", Directory.Exists(Path.Combine(dflt, "Shaders")), null);
            Report("Default\\Textures 存在", Directory.Exists(Path.Combine(dflt, "Textures")), null);
            Report("路径正确", dflt.EndsWith(@"Shader\YuanShen\Default", StringComparison.OrdinalIgnoreCase), dflt);

            // ---- B：Create ----
            Console.WriteLine();
            Console.WriteLine("B. Create 新建空预设");
            string err;
            bool existed;
            bool ok = PresetManager.Create("YuanShen", "MyPack", out err, out existed);
            Report("创建成功", ok && !existed, err);
            string myDir = PresetManager.PresetDir("YuanShen", "MyPack");
            Report("Shaders 已建", Directory.Exists(Path.Combine(myDir, "Shaders")), null);
            Report("Textures 已建", Directory.Exists(Path.Combine(myDir, "Textures")), null);

            PresetManager.Create("YuanShen", "MyPack", out err, out existed);
            Report("重复创建返回 existed", existed, null);

            PresetManager.Create("YuanShen", "", out err, out existed);
            Report("空名被拒", !string.IsNullOrEmpty(err), err);

            // ---- C：List ----
            Console.WriteLine();
            Console.WriteLine("C. List 列表");
            var list = PresetManager.List("YuanShen");
            var names = new List<string>();
            for (int i = 0; i < list.Count; i++) names.Add(list[i].Name);
            Report("含 Default 与 MyPack", names.Contains("Default") && names.Contains("MyPack"),
                   string.Join(", ", names.ToArray()));
            Report("Default 排第一", list.Count > 0 && list[0].Name == "Default",
                   list.Count > 0 ? list[0].Name : "(空)");

            // ---- D：ImportFromFolder（嵌套 reshade-shaders） ----
            Console.WriteLine();
            Console.WriteLine("D. ImportFromFolder 嵌套结构");
            string src = Path.Combine(root, "mypack-src");
            string nest = Path.Combine(src, "reshade-shaders");
            Directory.CreateDirectory(Path.Combine(nest, "Shaders", "Sub"));
            Directory.CreateDirectory(Path.Combine(nest, "Textures"));
            File.WriteAllText(Path.Combine(nest, "Shaders", "a.fx"), "// fx");
            File.WriteAllText(Path.Combine(nest, "Shaders", "Sub", "b.fx"), "// fx");
            File.WriteAllText(Path.Combine(nest, "Textures", "c.png"), "png");
            File.WriteAllText(Path.Combine(src, "readme.txt"), "x");

            int copied = PresetManager.ImportFromFolder("YuanShen", "Imported", src, out err);
            Report("复制 3 个文件", copied == 3, "copied=" + copied + " err=" + err);

            string importedDir = PresetManager.PresetDir("YuanShen", "Imported");
            var info = PresetManager.Inspect(importedDir);
            Report("2 个效果", info != null && info.ShaderCount == 2, info == null ? "null" : info.ShaderCount.ToString());
            Report("1 个纹理", info != null && info.TextureCount == 1, info == null ? "null" : info.TextureCount.ToString());
            Report("Display 可读", info != null && info.Display.Length > 0, info == null ? "" : info.Display);
            Report("Status=可用", info != null && info.Status == "可用", info == null ? "" : info.Status);
            Report("DisplayBrush 非空", info != null && info.DisplayBrush != null, null);

            // 认不出 Shaders/Textures 时必须报错且不建目录
            string junk = Path.Combine(root, "junk-src");
            Directory.CreateDirectory(junk);
            File.WriteAllText(Path.Combine(junk, "x.txt"), "x");
            int bad = PresetManager.ImportFromFolder("YuanShen", "Junk", junk, out err);
            Report("无滤镜内容被拒", bad == 0 && !string.IsNullOrEmpty(err), err);

            // ---- E：GetCurrent / SetCurrent ----
            Console.WriteLine();
            Console.WriteLine("E. 每个游戏独立记当前预设");
            Report("默认是 Default", PresetManager.GetCurrent("YuanShen") == "Default",
                   PresetManager.GetCurrent("YuanShen"));

            PresetManager.SetCurrent("YuanShen", "Imported");
            PresetManager.SetCurrent("ZenlessZoneZero", "Other");

            Report("YuanShen 记住 Imported",
                   PresetManager.GetCurrent("YuanShen") == "Imported", PresetManager.GetCurrent("YuanShen"));
            Report("绝区零互不影响（未建的预设名回落 Default）",
                   PresetManager.GetCurrent("ZenlessZoneZero") == "Default",
                   PresetManager.GetCurrent("ZenlessZoneZero"));
            Report("ZenlessZoneZero 的 settings 行独立",
                   !PresetManager.GetCurrent("ZenlessZoneZero").Equals("Imported"), null);

            // ---- F：Resolve ----
            Console.WriteLine();
            Console.WriteLine("F. Resolve 取预设目录");
            string sd, td;
            PresetManager.Resolve("YuanShen", "Imported", out sd, out td);
            Report("Shaders 目录有值", sd != null && Directory.Exists(sd), sd);
            Report("Textures 目录有值", td != null && Directory.Exists(td), td);

            PresetManager.Resolve("YuanShen", "NotExist", out sd, out td);
            Report("不存在的预设返回 null", sd == null && td == null, null);

            // ---- G：Sanitize / GameKey ----
            Console.WriteLine();
            Console.WriteLine("G. 名称合法化");
            Report("去 .exe", PresetManager.GameKey("YuanShen.exe") == "YuanShen", PresetManager.GameKey("YuanShen.exe"));
            Report("去非法字符", PresetManager.Sanitize("a/b:c*d") == "abcd", PresetManager.Sanitize("a/b:c*d"));
            Report("空名 → Default", PresetManager.GameKey("") == "Default", PresetManager.GameKey(""));

            // ---- H：zip 解压可用（验证 CS1701 绑定警告不影响运行） ----
            Console.WriteLine();
            Console.WriteLine("H. ZipFile 解压");
            string zipPath = Path.Combine(root, "pack.zip");
            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(src, zipPath);

                string unzip = Path.Combine(root, "unzipped");
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, unzip);

                bool hasFx = File.Exists(Path.Combine(unzip, "reshade-shaders", "Shaders", "a.fx"));
                Report("ZipFile 往返可用", hasFx, hasFx ? null : "解出来没找到 a.fx");

                int fromZip = PresetManager.ImportFromFolder("YuanShen", "FromZip", unzip, out err);
                Report("压缩包内容可导入", fromZip == 3, "copied=" + fromZip + " err=" + err);
            }
            catch (Exception ex)
            {
                Report("ZipFile 可用", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ---- I：IniPatcher 按预设写路径 ----
            Console.WriteLine();
            Console.WriteLine("I. IniPatcher 按选中预设写 ini");
            string gameDir = Path.Combine(root, "GameDir");
            Directory.CreateDirectory(gameDir);

            // 让游戏目录里先有用户自定义样式，验证不被碰
            string iniPath = Path.Combine(gameDir, "ReShade.ini");
            File.WriteAllText(iniPath,
                "[GENERAL]\r\nEffectSearchPaths=C:\\old\\\r\nTextureSearchPaths=C:\\old\\\r\n" +
                "[STYLE]\r\nFontSize=17\r\nStyleIndex=2\r\n" +
                "[INPUT]\r\nKeyOverlay=36,0,0,0\r\n", new UTF8Encoding(false));

            List<string> report;
            string ierr;
            bool iniOk = IniPatcher.Ensure(gameDir, "YuanShen", "Imported", out report, out ierr);
            Report("Ensure 成功", iniOk, ierr);

            string text = File.ReadAllText(iniPath, Encoding.UTF8);

            // 跨盘符时是绝对路径，同盘符是相对；两种都算对，只要指到 Imported
            bool effectOk = text.Contains(@"Imported\Shaders");
            bool textureOk = text.Contains(@"Imported\Textures");
            Report("EffectSearchPaths 指向预设", effectOk, GrabValue(text, "EffectSearchPaths"));
            Report("TextureSearchPaths 指向预设", textureOk, GrabValue(text, "TextureSearchPaths"));
            Report("不再指向旧的 C:\\old", !text.Contains(@"C:\old"), null);
            Report("样式 FontSize=17 保留", text.Contains("FontSize=17"), null);
            Report("样式 StyleIndex=2 保留", text.Contains("StyleIndex=2"), null);
            Report("快捷键 KeyOverlay 保留", text.Contains("KeyOverlay=36,0,0,0"), null);
            Report("CRLF 行尾保住", text.Contains("\r\n") && !text.Replace("\r\n", "").Contains("\n"), null);

            // ---- 结果 ----
            Console.WriteLine();
            if (_fail == 0)
            {
                Console.WriteLine("RESULT: ALL PASS");
                return 0;
            }
            Console.WriteLine("RESULT: " + _fail + " FAILED");
            return 1;
        }
    }
}