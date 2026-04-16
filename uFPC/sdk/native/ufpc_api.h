#ifndef UFPC_API_H
#define UFPC_API_H

#include <stdint.h>

#ifdef _WIN32
#  ifdef UFPCBRIDGE_BUILD
#    define UFPC_API __declspec(dllexport)
#  else
#    define UFPC_API __declspec(dllimport)
#  endif
#  define UFPC_CALL __stdcall
#else
#  define UFPC_API
#  define UFPC_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum uFpcApiStatus
{
    UFPC_API_OK = 0,
    UFPC_API_E_INVALID_ARGUMENT = 1,
    UFPC_API_E_LAUNCH_FAILED = 2,
    UFPC_API_E_INTERNAL_ERROR = 3,
    UFPC_API_E_NOT_IMPLEMENTED = 4
};

enum uFpcDebugBackend
{
    UFPC_DEBUG_BACKEND_NONE = 0,
    UFPC_DEBUG_BACKEND_GDB_MI = 1
};

enum uFpcDebugFormat
{
    UFPC_DEBUG_FORMAT_NONE = 0,
    UFPC_DEBUG_FORMAT_DWARF = 1
};

enum uFpcDebugCapability
{
    UFPC_DEBUG_CAP_BREAKPOINTS = 1u << 0,
    UFPC_DEBUG_CAP_WATCHES = 1u << 1,
    UFPC_DEBUG_CAP_REGISTERS = 1u << 2,
    UFPC_DEBUG_CAP_CALLSTACK = 1u << 3,
    UFPC_DEBUG_CAP_SOURCE_PATHS = 1u << 4
};

typedef struct uFpcRunResult
{
    uint32_t struct_size;
    int32_t api_status;
    int32_t compiler_exit_code;
    uint32_t output_chars;
} uFpcRunResult;

typedef struct uFpcDebugSessionRequest
{
    uint32_t struct_size;
    const wchar_t* gdb_path;
    const wchar_t* executable_path;
    const wchar_t* working_directory;
    const wchar_t* source_directory;
    uint32_t preferred_backend;
} uFpcDebugSessionRequest;

typedef struct uFpcDebugSessionInfo
{
    uint32_t struct_size;
    uint32_t backend;
    uint32_t debug_format;
    uint32_t capabilities;
} uFpcDebugSessionInfo;

UFPC_API uint32_t UFPC_CALL uFPC_GetApiVersion(void);
UFPC_API int32_t UFPC_CALL uFPC_RunCompiler(
    const wchar_t* compiler_path,
    const wchar_t* working_directory,
    const wchar_t* command_line,
    uFpcRunResult* result_info);
UFPC_API int32_t UFPC_CALL uFPC_CreateHostExecutable(
    const wchar_t* runtime_image_path,
    const wchar_t* executable_path);
UFPC_API uint32_t UFPC_CALL uFPC_CopyLastOutput(wchar_t* buffer, uint32_t buffer_chars);
UFPC_API uint32_t UFPC_CALL uFPC_CopyLastError(wchar_t* buffer, uint32_t buffer_chars);
UFPC_API int32_t UFPC_CALL uFPC_GetDebugArchitecture(uFpcDebugSessionInfo* session_info);
UFPC_API int32_t UFPC_CALL uFPC_OpenDebugSession(
    const uFpcDebugSessionRequest* request,
    uint64_t* session_handle);
UFPC_API int32_t UFPC_CALL uFPC_CloseDebugSession(uint64_t session_handle);

#ifdef __cplusplus
}
#endif

#endif
