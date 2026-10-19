// CI-only, unmanaged shutdown diagnostics. No callbacks into the terminating CLR.
#include <windows.h>
#include <stdio.h>
static wchar_t logPath[32768];
static LONG recording;
static LONG CALLBACK recordFault(EXCEPTION_POINTERS* exception) {
    if (exception->ExceptionRecord->ExceptionCode != 0xc000000d || InterlockedExchange(&recording, 1)) return EXCEPTION_CONTINUE_SEARCH;
    HANDLE file = CreateFileW(logPath, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return EXCEPTION_CONTINUE_SEARCH;
    void* frames[80]; USHORT count = CaptureStackBackTrace(0, 80, frames, nullptr);
    for (USHORT i = 0; i < count; ++i) {
        HMODULE module = nullptr; char path[MAX_PATH] = "unknown", line[1024];
        GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCSTR)frames[i], &module);
        if (module) GetModuleFileNameA(module, path, MAX_PATH);
        int length = sprintf_s(line, "%s+0x%llX\r\n", path, (unsigned long long)((ULONG_PTR)frames[i] - (ULONG_PTR)module));
        DWORD written; if (length > 0) WriteFile(file, line, (DWORD)length, &written, nullptr);
    }
    CloseHandle(file); return EXCEPTION_CONTINUE_SEARCH;
}
extern "C" __declspec(dllexport) void Install(const wchar_t* directory) {
    swprintf_s(logPath, L"%s\\native-stack.txt", directory);
    AddVectoredExceptionHandler(1, recordFault);
}
