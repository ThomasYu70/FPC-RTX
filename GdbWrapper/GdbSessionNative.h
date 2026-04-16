#pragma once
// GDB 프로세스 관리 — 네이티브 C++ (Win32 파이프, CLR 없음)

#include <Windows.h>
#include <string>
#include <functional>
#include <thread>
#include <atomic>
#include <mutex>
#include "GdbMiNative.h"

namespace GdbNative {

// ── 이벤트 데이터 구조체 ──────────────────────────────────────────────────

struct StopInfo {
    std::string reason;      // "breakpoint-hit", "end-stepping-range", …
    std::string fileName;    // fullname 또는 file
    int         line     = 0;
    std::string function;
    std::string address;
    int         bpNumber = 0;
    int         exitCode = 0;
};

// ── 콜백 타입 ─────────────────────────────────────────────────────────────

using StoppedCb = std::function<void(const StopInfo&)>;
using ConsoleCb = std::function<void(const std::string&)>;
using ExitedCb  = std::function<void(int)>;
using ResultCb  = std::function<void(const GdbMi::ResultRecord&)>;

// ── 네이티브 GDB 세션 ─────────────────────────────────────────────────────

class GdbSession {
public:
    // 콜백 (Start() 전에 설정)
    StoppedCb onStopped;
    ConsoleCb onConsole;
    ExitedCb  onExited;
    ResultCb  onResult;

    GdbSession();
    ~GdbSession();

    bool start(const std::wstring& gdbPath);
    void stop();
    bool isRunning() const { return _running.load(); }

    // MI 명령 전송 → 사용된 토큰 반환
    int sendMi(const std::string& command);

    // 래퍼 명령
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
