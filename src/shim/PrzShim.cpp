// PrzShim.dll – native COM-Brücke für die PRZ-Vorschau im MSIX-Paket.
//
// MSIX kann nur native DLLs als COM-Klasse eintragen. Diese DLL startet die in Windows
// enthaltene .NET-Framework-4-Laufzeit und erzeugt darin den verwalteten Vorschau-Handler
// aus PrzPreview.dll (gleicher Ordner). Explorer spricht danach direkt mit dem .NET-Objekt.
//
// Ablauf: DllGetClassObject -> ClassFactory::CreateInstance
//         -> ICLRRuntimeHost::ExecuteInDefaultAppDomain(PrzPreview.dll, "PrzPreview.ShimEntry", "Create", "<Adresse>")
//         -> ShimEntry.Create schreibt den IUnknown-Zeiger des Handlers an <Adresse>.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <mscoree.h>
#include <metahost.h>
#include <stdio.h>
#include <new>

// CLSID dieser Klasse – muss mit AppxManifest.xml übereinstimmen.
// {A62E1A79-F1D7-488F-88B7-B99FD80C7BFE}
static const CLSID CLSID_PrzShim = { 0xa62e1a79, 0xf1d7, 0x488f, { 0x88, 0xb7, 0xb9, 0x9f, 0xd8, 0x0c, 0x7b, 0xfe } };

// Bekannte Kennungen der CLR-Hosting-Schnittstellen (eigene Konstanten, damit kein mscoree.lib nötig ist)
static const CLSID kCLSID_CLRMetaHost     = { 0x9280188d, 0x0e8e, 0x4867, { 0xb3, 0x0c, 0x7f, 0xa8, 0x38, 0x84, 0xe8, 0xde } };
static const IID   kIID_ICLRMetaHost      = { 0xd332db9e, 0xb9b3, 0x4125, { 0x82, 0x07, 0xa1, 0x48, 0x84, 0xf5, 0x32, 0x16 } };
static const IID   kIID_ICLRRuntimeInfo   = { 0xbd39d1d2, 0xba2f, 0x486a, { 0x89, 0xb0, 0xb4, 0xb0, 0xcb, 0x46, 0x68, 0x91 } };
static const CLSID kCLSID_CLRRuntimeHost  = { 0x90f1a06e, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };
static const IID   kIID_ICLRRuntimeHost   = { 0x90f1a06c, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };

typedef HRESULT (STDAPICALLTYPE *PFN_CLRCreateInstance)(REFCLSID, REFIID, LPVOID*);

static HMODULE           g_module = nullptr;
static wchar_t           g_assembly[MAX_PATH];        // ...\PrzPreview.dll
static ICLRRuntimeHost*  g_host = nullptr;
static CRITICAL_SECTION  g_lock;

// Kurzes Fehlerprotokoll nach %LOCALAPPDATA%Low\PrzPreview.log (wie der verwaltete Teil)
static void Log(const wchar_t* msg, HRESULT hr)
{
    wchar_t path[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH);
    if (n == 0 || n > MAX_PATH - 32) return;
    wcscat_s(path, L"Low\\PrzPreview.log");
    HANDLE f = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME t; GetLocalTime(&t);
    char line[512];
    int len = sprintf_s(line, "%02d:%02d:%02d.%03d [shim] %ls (0x%08lX)\r\n",
                        t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, msg, (unsigned long)hr);
    DWORD written;
    if (len > 0) WriteFile(f, line, (DWORD)len, &written, nullptr);
    CloseHandle(f);
}

// Startet die .NET-4-Laufzeit einmalig (bzw. hängt sich an eine bereits geladene an)
static HRESULT EnsureRuntime()
{
    if (g_host) return S_OK;

    HMODULE mscoree = LoadLibraryW(L"mscoree.dll");
    if (!mscoree) return HRESULT_FROM_WIN32(GetLastError());
    auto create = (PFN_CLRCreateInstance)GetProcAddress(mscoree, "CLRCreateInstance");
    if (!create) return HRESULT_FROM_WIN32(GetLastError());

    ICLRMetaHost* meta = nullptr;
    HRESULT hr = create(kCLSID_CLRMetaHost, kIID_ICLRMetaHost, (void**)&meta);
    if (FAILED(hr)) { Log(L"CLRCreateInstance", hr); return hr; }

    ICLRRuntimeInfo* info = nullptr;
    hr = meta->GetRuntime(L"v4.0.30319", kIID_ICLRRuntimeInfo, (void**)&info);
    meta->Release();
    if (FAILED(hr)) { Log(L"GetRuntime v4.0.30319", hr); return hr; }

    ICLRRuntimeHost* host = nullptr;
    hr = info->GetInterface(kCLSID_CLRRuntimeHost, kIID_ICLRRuntimeHost, (void**)&host);
    info->Release();
    if (FAILED(hr)) { Log(L"GetInterface ICLRRuntimeHost", hr); return hr; }

    hr = host->Start();   // S_FALSE/Fehler, wenn schon gestartet – dann trotzdem weiter
    if (FAILED(hr)) Log(L"Start (wird ignoriert, Laufzeit evtl. schon aktiv)", hr);

    g_host = host;
    return S_OK;
}

// Erzeugt den verwalteten Handler und liefert seinen IUnknown-Zeiger (AddRef'd)
static HRESULT CreateManagedHandler(IUnknown** out)
{
    *out = nullptr;
    EnterCriticalSection(&g_lock);
    HRESULT hr = EnsureRuntime();
    LeaveCriticalSection(&g_lock);
    if (FAILED(hr)) return hr;

    IUnknown* unk = nullptr;
    wchar_t arg[32];
    swprintf_s(arg, L"%llx", (unsigned long long)(ULONG_PTR)&unk);

    DWORD ret = 1;
    hr = g_host->ExecuteInDefaultAppDomain(g_assembly, L"PrzPreview.ShimEntry", L"Create", arg, &ret);
    if (FAILED(hr)) { Log(L"ExecuteInDefaultAppDomain", hr); return hr; }
    if (ret != 0 || !unk) { Log(L"ShimEntry.Create meldet Fehler", E_FAIL); return E_FAIL; }

    *out = unk;
    return S_OK;
}

// ---------------------------------------------------------------------------------------
class ClassFactory : public IClassFactory
{
    LONG m_ref = 1;
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) { *ppv = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    STDMETHODIMP_(ULONG) Release() override
    {
        LONG r = InterlockedDecrement(&m_ref);
        if (r == 0) delete this;
        return r;
    }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        IUnknown* handler = nullptr;
        HRESULT hr = CreateManagedHandler(&handler);
        if (FAILED(hr)) return hr;
        hr = handler->QueryInterface(riid, ppv);
        handler->Release();
        if (FAILED(hr)) Log(L"QueryInterface auf Handler", hr);
        return hr;
    }
    STDMETHODIMP LockServer(BOOL) override { return S_OK; }
};

// ---------------------------------------------------------------------------------------
extern "C" BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = inst;
        DisableThreadLibraryCalls(inst);
        InitializeCriticalSection(&g_lock);
        // Pfad zu PrzPreview.dll = Ordner dieser DLL
        DWORD n = GetModuleFileNameW(inst, g_assembly, MAX_PATH);
        if (n == 0 || n >= MAX_PATH) return FALSE;
        wchar_t* slash = wcsrchr(g_assembly, L'\\');
        if (!slash) return FALSE;
        *(slash + 1) = 0;
        if (wcscat_s(g_assembly, L"PrzPreview.dll") != 0) return FALSE;
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_PrzShim) return CLASS_E_CLASSNOTAVAILABLE;
    ClassFactory* cf = new (std::nothrow) ClassFactory();
    if (!cf) return E_OUTOFMEMORY;
    HRESULT hr = cf->QueryInterface(riid, ppv);
    cf->Release();
    return hr;
}

// Die .NET-Laufzeit lässt sich nicht entladen – DLL bleibt geladen, bis der Vorschau-Prozess endet.
STDAPI DllCanUnloadNow() { return S_FALSE; }
