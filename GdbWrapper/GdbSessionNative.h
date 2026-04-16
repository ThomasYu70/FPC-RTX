#pragma once
// GDB process manager -- native C++ (Win32 pipes, no CLR)

#include <Windows.h>
#include <string>
#include <functional>
#include <thread>
#include <atomic>
#include <mutex>
#include "GdbMiNative.h"

namespace GdbNative {

// ── Event data ────────────────────────────────────────────────────────────

struct StopInfo {
    std::string reason;     // "breakpoint-hit", "end-stepping-range", ...
    std::string fileName;   // fullname or file
    int         line     = 0;
    std::string function;
    std::string address;
    int         bpNumber = 0;
    int         exitCode = 0;
};

// ── Callback types ────────────────────────────────────────────────────────

using StoppedCb = std::function<void(const StopInfo&)>;
using ConsoleCb = std::function<void(const std::string&)>;
using ExitedCb  = std::function<void(int)>;
using ResultCb  = std::function<void(const GdbMi::ResultRecord&)>;

// ── Native GDB session ────────────────────────────────────────────────────

class GdbSession {
public:
    // Set before Start()
    StoppedCb onStopped;
    ConsoleCb onConsole;
    ExitedCb  onExited;
    ResultCb  onResult;

    GdbSession();
    ~GdbSession();

    bool start(const std::wstring& gdbPath);
    void stop();
    bool isRunning() const { return _running.load(); }

    // Send raw MI command, returns token
    int sendMi(const std::string& command);

    // Convenience wrappers
    void loadExe(const std::string& path, const std::string& workDir);
    void execRun();
    void execContinue();
    void execNext();
    void execStep();
    void execFinish();
    void execInterrupt();
    void execUntil(const std::string& file, int line);
    int  breakInsert(const std::string& file, int line);
    void breakDelete(int id);
    void breakEnable(int id);
    void breakDisable(int id);
    void breakCondition(int id, const std::string& cond);
    int  dataEvaluate(const std::string& expr);
    void stackListFrames();
    void listRegisterNames();
    void listRegisterValues();
    void envDirectory(const std::string& dir);
    void envCd(const std::string& dir);

private:
    void readLoop();
    void dispatchLine(const GdbMi::MiLine& line);
    StopInfo extractStop(const GdbMi::AsyncRecord& rec);
    static std::string toGdbPath(const std::string& winPath);

    HANDLE _hProcess    = INVALID_HANDLE_VALUE;
    HANDLE _hThread     = INVALID_HANDLE_VALUE;
    HANDLE _hStdinWrite = INVALID_HANDLE_VALUE;
    HANDLE _hStdoutRead = INVALID_HANDLE_VALUE;
    HANDLE _hStderrRead = INVALID_HANDLE_VALUE;

    std::thread       _readerThread;
    std::thread       _stderrThread;
    std::atomic<bool> _running{false};
    std::atomic<int>  _token{1};
    std::mutex        _writeMutex;
};

} // namespace GdbNative
