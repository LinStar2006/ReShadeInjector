// D3D11 靶子：创建一个真实的 D3D11 swapchain 并持续 Present，
// 用于验证「ReShade 注入到图形进程后能否 hook 成功」，以及确认
// ReShade 到底在哪个目录找 ReShade.ini / reshade-shaders。
//
// 时序刻意设计：先写 PID、再 Sleep 一段时间留出注入窗口、然后才创建
// D3D 设备。这样注入器（或 ReShade 的 HookManager）能拦到 CreateSwapChain。
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <cstdio>
#include <cwchar>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "user32.lib")

static ID3D11Device*            g_device = nullptr;
static ID3D11DeviceContext*     g_context = nullptr;
static IDXGISwapChain*          g_swapChain = nullptr;
static ID3D11RenderTargetView*  g_rtv = nullptr;

static void WriteSelfFile(const wchar_t* name, const char* text)
{
    wchar_t path[MAX_PATH];
    DWORD n = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (n == 0) return;
    wchar_t* slash = wcsrchr(path, L'\\');
    if (slash) *(slash + 1) = L'\0';
    lstrcatW(path, name);

    HANDLE h = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(h, text, (DWORD)lstrlenA(text), &written, nullptr);
    CloseHandle(h);
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_DESTROY) { PostQuitMessage(0); return 0; }
    if (msg == WM_CLOSE)   { DestroyWindow(hwnd); return 0; }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

static bool CreateGraphics(HWND hwnd)
{
    DXGI_SWAP_CHAIN_DESC sd = {};
    sd.BufferCount = 2;
    sd.BufferDesc.Width = 0;
    sd.BufferDesc.Height = 0;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferDesc.RefreshRate.Numerator = 60;
    sd.BufferDesc.RefreshRate.Denominator = 1;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hwnd;
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
        nullptr, 0, D3D11_SDK_VERSION, &sd,
        &g_swapChain, &g_device, nullptr, &g_context);
    printf("D3D11CreateDeviceAndSwapChain hr=0x%08X\n", (unsigned)hr);
    if (FAILED(hr)) return false;

    ID3D11Texture2D* backBuffer = nullptr;
    hr = g_swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&backBuffer);
    if (FAILED(hr) || backBuffer == nullptr) return false;

    hr = g_device->CreateRenderTargetView(backBuffer, nullptr, &g_rtv);
    backBuffer->Release();
    printf("CreateRenderTargetView hr=0x%08X\n", (unsigned)hr);
    return SUCCEEDED(hr);
}

int main()
{
    char pidText[32];
    sprintf_s(pidText, "%lu\n", GetCurrentProcessId());
    WriteSelfFile(L"d3dtarget-pid.txt", pidText);
    printf("d3d-target pid=%lu\n", GetCurrentProcessId());
    fflush(stdout);

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"D3DTargetWnd";
    RegisterClassExW(&wc);

    // 用 WS_POPUP 保持朴素，避免额外样式影响 hook
    HWND hwnd = CreateWindowExW(0, L"D3DTargetWnd", L"d3d-target", WS_OVERLAPPEDWINDOW,
                                100, 100, 800, 600, nullptr, nullptr, wc.hInstance, nullptr);
    if (!hwnd) { printf("CreateWindow 失败\n"); return 2; }
    ShowWindow(hwnd, SW_SHOWNORMAL);
    UpdateWindow(hwnd);

    // 留出注入窗口：注入器（或手工）在这几秒内把 ReShade 注进来
    printf("waiting 8s for injection...\n");
    fflush(stdout);
    Sleep(8000);

    if (!CreateGraphics(hwnd)) { printf("图形初始化失败\n"); return 3; }

    const float clear[4] = { 0.10f, 0.12f, 0.18f, 1.0f };
    printf("presenting frames (close the window to quit)...\n");
    fflush(stdout);

    // 一直 Present，直到用户关窗口 / 进程被结束。
    // 之前的版本跑 900 帧就自己退了，测试时窗口一闪就没了，不方便观察。
    bool quit = false;
    int frame = 0;
    while (!quit)
    {
        MSG msg;
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
        {
            if (msg.message == WM_QUIT) { quit = true; break; }
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        if (quit) break;

        g_context->ClearRenderTargetView(g_rtv, clear);
        g_context->OMSetRenderTargets(1, &g_rtv, nullptr);
        g_swapChain->Present(1, 0);

        if ((frame % 300) == 0) { printf("frame %d\n", frame); fflush(stdout); }
        ++frame;
    }

    printf("渲染结束\n");
    fflush(stdout);

    if (g_rtv) g_rtv->Release();
    if (g_swapChain) g_swapChain->Release();
    if (g_context) g_context->Release();
    if (g_device) g_device->Release();
    return 0;
}