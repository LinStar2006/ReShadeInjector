// 测试用 DLL：被注入后往【宿主进程 exe 所在目录】写一个 injected-marker.txt，
// 证明 DllMain 真的在目标进程里跑起来了，而不只是 CreateRemoteThread 返回成功。
//
// 两个实测踩到的坑（改这个文件前务必先看）：
// 1. 取「本模块」路径**必须用 DllMain 传进来的 hModule**：
//    `GetModuleFileNameW((HMODULE)(void*)&SomeFunc, ...)` 在 /O2 下会返回 0、
//    GetLastError 为 126（ERROR_MOD_NOT_FOUND），因为函数折叠让该地址归属不到本模块。
//    取宿主路径则用 GetModuleFileNameW(NULL, ...)。
// 2. 别用 CRT 的 `_wfopen_s` / `fprintf` 写文件——在本机实测会静默失败（文件根本不被创建），
//    换成 CreateFileW / WriteFile 一次就成。
#include <windows.h>

static void AppendStr(wchar_t* dst, int cap, int* pos, const wchar_t* src)
{
    while (*src && *pos < cap - 1) dst[(*pos)++] = *src++;
    dst[*pos] = L'\0';
}

static void AppendUInt(wchar_t* dst, int cap, int* pos, DWORD v)
{
    wchar_t tmp[16];
    int n = 0;
    if (v == 0) { tmp[n++] = L'0'; }
    else { while (v > 0) { tmp[n++] = (wchar_t)(L'0' + (v % 10)); v /= 10; } }
    while (n > 0 && *pos < cap - 1) dst[(*pos)++] = tmp[--n];
    dst[*pos] = L'\0';
}

static void Append2(wchar_t* dst, int cap, int* pos, DWORD v)
{
    wchar_t tmp[2];
    tmp[0] = (wchar_t)(L'0' + (v / 10) % 10);
    tmp[1] = (wchar_t)(L'0' + v % 10);
    if (*pos < cap - 1) dst[(*pos)++] = tmp[0];
    if (*pos < cap - 1) dst[(*pos)++] = tmp[1];
    dst[*pos] = L'\0';
}

static void WriteMarker(HMODULE hModule)
{
    // marker 写在【宿主进程 exe 所在目录】，也就是被注入的那个程序旁边 ——
    // 这样用户一眼就知道"这个 marker 是属于哪个程序的"。
    // 读不到宿主路径（理论上不该发生）再退回 DLL 自己的目录。
    wchar_t dir[MAX_PATH];
    DWORD n = GetModuleFileNameW(NULL, dir, MAX_PATH);
    if (n == 0 || n >= MAX_PATH || wcsrchr(dir, L'\\') == NULL)
    {
        n = GetModuleFileNameW(hModule, dir, MAX_PATH);
        if (n == 0 || n >= MAX_PATH) return;
    }

    wchar_t* slash = wcsrchr(dir, L'\\');
    if (slash) *(slash + 1) = L'\0';

    const wchar_t* name = L"injected-marker.txt";
    wchar_t path[MAX_PATH];
    int pos = 0;
    AppendStr(path, MAX_PATH, &pos, dir);
    AppendStr(path, MAX_PATH, &pos, name);

    HANDLE h = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return;

    SYSTEMTIME st;
    GetLocalTime(&st);

    wchar_t line[256];
    int p = 0;
    AppendStr(line, 256, &p, L"injected ok, target pid=");
    AppendUInt(line, 256, &p, GetCurrentProcessId());
    AppendStr(line, 256, &p, L", time=");
    AppendUInt(line, 256, &p, st.wYear);
    AppendStr(line, 256, &p, L"-");
    Append2(line, 256, &p, st.wMonth);
    AppendStr(line, 256, &p, L"-");
    Append2(line, 256, &p, st.wDay);
    AppendStr(line, 256, &p, L" ");
    Append2(line, 256, &p, st.wHour);
    AppendStr(line, 256, &p, L":");
    Append2(line, 256, &p, st.wMinute);
    AppendStr(line, 256, &p, L":");
    Append2(line, 256, &p, st.wSecond);
    AppendStr(line, 256, &p, L"\r\n");

    DWORD written = 0;
    // 写入 UTF-16LE（带 BOM），方便记事本/VS Code 直接认得
    const wchar_t bom = 0xFEFF;
    WriteFile(h, &bom, sizeof(bom), &written, NULL);
    WriteFile(h, line, (DWORD)(p * sizeof(wchar_t)), &written, NULL);
    CloseHandle(h);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        WriteMarker(hModule);
    }
    return TRUE;
}

__declspec(dllexport) int TestDllVersion(void)
{
    return 1;
}