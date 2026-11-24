using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HoYoInjector
{
    /// <summary>
    /// ReShade64.dll 有一个 loading check：目标进程 exe 目录下没有 ReShade.ini 时，
    /// 它的 DllMain 会直接返回 FALSE，注入器只能看到「LoadLibraryW 返回 0」。
    /// 这个类提供一份内置的最小可用 ini 模板，在用户什么配置都没准备时写出去，
    /// 让「只放一个 ReShade64.dll」也能注入成功。
    ///
    /// 模板里的路径全部用相对写法（.\reshade-shaders\Shaders\ 之类），
    /// 因此复制到任意游戏目录都不会失效；代价是目标目录没有 shaders 文件夹时
    /// 只有 overlay 菜单，没有画面效果。
    /// </summary>
    internal static class DefaultIni
    {
        private const string ResourceName = "HoYoInjector.default-reshade.ini";
        private const string FileName = "ReShade.ini";

        /// <summary>把内置模板写到 targetDir\ReShade.ini。已存在则不覆盖。</summary>
        public static bool WriteTo(string targetDir, out string error)
        {
            error = null;
            string dest = Path.Combine(targetDir, FileName);

            if (File.Exists(dest))
            {
                error = "目标目录已有 ReShade.ini，不覆盖：" + dest;
                return false;
            }

            try
            {
                string text = ReadTemplate();
                // 不写 BOM：ReShade 按自己的规则解析 ini，纯 ASCII 内容最保险
                File.WriteAllText(dest, text, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ReadTemplate()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(ResourceName))
            {
                if (s == null)
                {
                    var names = string.Join(", ", asm.GetManifestResourceNames());
                    throw new InvalidOperationException(
                        "找不到嵌入资源 " + ResourceName + "。实际资源：[" + names + "]");
                }
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
        }
    }
}