// 测试靶子（无图形）：写一个 PID 文件，然后开一个小窗口一直挂着。
// 关掉窗口（或任务管理器结束进程）才退出 —— 方便注入后反复观察，
// 也方便验证「进程消失后注入器能不能正确显示未运行」。
#include <windows.h>
#include <stdio.h>
#include <wchar.h>

static const wchar_t* kClassName = L"ReShadeTestTargetWnd";

static void WritePidFile(void)
{
    wchar_t dir[MAX_PATH];
    DWORD n = GetModuleFileNameW(NULL, dir, MAX_PATH);
    if (n == 0) return;

    wchar_t* slash = wcsrchr(dir, L'\\');
    if (slash) *(slash + 1) = L'\0';

    wchar_t path[MAX_PATH];
    _snwprintf_s(path, MAX_PATH, _TRUNCATE, L"%starget-pid.txt", dir);

    FILE* f = NULL;
    if (_wfopen_s(&f, path, L"wb") != 0 || f == NULL) return;
    fprintf(f, "%lu\n", GetCurrentProcessId());
    fclose(f);
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_DESTROY) { PostQuitMessage(0); return 0; }
    if (msg == WM_CLOSE)   { DestroyWindow(hwnd); return 0; }
    if (msg == WM_PAINT)
    {
        PAINTSTRUCT ps;
        HDC dc = BeginPaint(hwnd, &ps);
        RECT rc;
        GetClientRect(hwnd, &rc);

        HBRUSH bg = CreateSolidBrush(RGB(0x14, 0x18, 0x20));
        FillRect(dc, &rc, bg);
        DeleteObject(bg);

        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(0xED, 0xF0, 0xF6));
        DrawTextW(dc, L"注入测试靶子", -1, &rc, DT_CENTER | DT_SINGLELINE | DT_TOP | DT_NOPREFIX);

        RECT rc2 = rc;
        rc2.top += 40;
        SetTextColor(dc, RGB(0x9A, 0xA5, 0xB8));
        DrawTextW(dc, L"这个进程会一直挂着。\n注入成功后同目录会生成 injected-marker.txt。\n关闭本窗口即退出。",
                  -1, &rc2, DT_CENTER | DT_WORDBREAK | DT_NOPREFIX);

        EndPaint(hwnd, &ps);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE hPrev, LPWSTR cmd, int show)
{
    (void)hPrev; (void)cmd; (void)show;

    WritePidFile();

    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = kClassName;
    wc.hCursor = LoadCursorW(NULL, IDC_ARROW);
    RegisterClassExW(&wc);

    HWND hwnd = CreateWindowExW(0, kClassName, L"测试靶子 · target.exe",
                                WS_OVERLAPPEDWINDOW & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME,
                                CW_USEDEFAULT, CW_USEDEFAULT, 420, 200,
                                NULL, NULL, hInstance, NULL);
    if (hwnd == NULL) return 2;

    ShowWindow(hwnd, SW_SHOWNORMAL);
    UpdateWindow(hwnd);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0)
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return 0;
}
