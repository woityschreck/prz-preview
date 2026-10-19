// ShimTest.exe <PrzShim.dll> <datei.prz> [sekunden]
// Testet die native Brücke wie prevhost.exe: nativer Prozess ohne geladene .NET-Laufzeit,
// DLL direkt laden (ohne Registrierung), Handler erzeugen, Vorschau in einem Fenster zeigen.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <shlwapi.h>
#include <shobjidl.h>
#include <propsys.h>
#include <stdio.h>

typedef HRESULT (STDAPICALLTYPE *PFN_DllGetClassObject)(REFCLSID, REFIID, LPVOID*);

static IPreviewHandler* g_preview = nullptr;

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg)
    {
    case WM_SIZE:
        if (g_preview) { RECT rc; GetClientRect(hwnd, &rc); g_preview->SetRect(&rc); }
        return 0;
    case WM_TIMER:
        PostQuitMessage(0);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

static int Fail(const char* step, HRESULT hr)
{
    printf("FEHLER bei %s: 0x%08lX\n", step, (unsigned long)hr);
    return 1;
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3) { printf("Aufruf: ShimTest.exe <PrzShim.dll> <datei.prz> [sekunden]\n"); return 2; }
    int seconds = argc > 3 ? _wtoi(argv[3]) : 8;

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) return Fail("CoInitializeEx", hr);

    printf(".NET vor dem Laden geladen: %s\n", GetModuleHandleW(L"clr.dll") ? "ja" : "nein");

    HMODULE shim = LoadLibraryW(argv[1]);
    if (!shim) return Fail("LoadLibrary", HRESULT_FROM_WIN32(GetLastError()));
    auto getClassObject = (PFN_DllGetClassObject)GetProcAddress(shim, "DllGetClassObject");
    if (!getClassObject) return Fail("GetProcAddress", E_FAIL);

    CLSID clsid;
    CLSIDFromString(L"{A62E1A79-F1D7-488F-88B7-B99FD80C7BFE}", &clsid);
    IClassFactory* factory = nullptr;
    hr = getClassObject(clsid, IID_IClassFactory, (void**)&factory);
    if (FAILED(hr)) return Fail("DllGetClassObject", hr);

    DWORD t0 = GetTickCount();
    hr = factory->CreateInstance(nullptr, IID_IPreviewHandler, (void**)&g_preview);
    factory->Release();
    if (FAILED(hr)) return Fail("CreateInstance (IPreviewHandler)", hr);
    printf("Handler erzeugt in %lu ms, .NET jetzt geladen: %s\n", GetTickCount() - t0, GetModuleHandleW(L"clr.dll") ? "ja" : "nein");

    IInitializeWithStream* init = nullptr;
    hr = g_preview->QueryInterface(IID_IInitializeWithStream, (void**)&init);
    if (FAILED(hr)) return Fail("QueryInterface IInitializeWithStream", hr);
    IStream* stream = nullptr;
    hr = SHCreateStreamOnFileEx(argv[2], STGM_READ | STGM_SHARE_DENY_WRITE, 0, FALSE, nullptr, &stream);
    if (FAILED(hr)) return Fail("SHCreateStreamOnFileEx", hr);
    hr = init->Initialize(stream, STGM_READ);
    stream->Release();
    init->Release();
    if (FAILED(hr)) return Fail("Initialize", hr);

    WNDCLASSW wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"PrzShimTest";
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    RegisterClassW(&wc);
    HWND hwnd = CreateWindowW(L"PrzShimTest", L"ShimTest (native Brücke)", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                              60, 60, 540, 780, nullptr, nullptr, wc.hInstance, nullptr);

    RECT rc; GetClientRect(hwnd, &rc);
    hr = g_preview->SetWindow(hwnd, &rc);
    if (FAILED(hr)) return Fail("SetWindow", hr);
    hr = g_preview->DoPreview();
    if (FAILED(hr)) return Fail("DoPreview", hr);
    printf("DoPreview OK – Fenster bleibt %d s offen\n", seconds);
    fflush(stdout);

    SetTimer(hwnd, 1, seconds * 1000, nullptr);
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }

    g_preview->Unload();
    g_preview->Release();
    printf("Ende OK\n");
    return 0;
}
