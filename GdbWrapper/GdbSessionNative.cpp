// GDB process manager implementation -- native C++ (Win32, no CLR)
#include "GdbSessionNative.h"
#include <algorithm>

namespace GdbNative {

GdbSession::GdbSession()  = default;
GdbSession::~GdbSession() { stop(); }

// ── Path conversion: backslash -> slash ───────────────────────────────────

std::string GdbSession::toGdbPath(const std::string& winPath) {
    std::string p = winPath;
    std::replace(p.begin(), p.end(), '\\', '/');
    return p;
}

// ── Start GDB process ─────────────────────────────────────────────────────

bool GdbSession::start(const std::wstring& gdbPath) {
    if (_running.load()) return false;

    SECURITY_ATTRIBUTES sa = { sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE };

    // Create stdin/stdout/stderr pipes
    HANDLE hStdinRead,   hStdinWriteTmp;
    HANDLE hStdoutWrite, hStdoutReadTmp;
    HANDLE hStderrWrite, hStderrReadTmp;

    if (!CreatePipe(&hStdinRead,    &hStdinWriteTmp, &sa, 0)) return false;
    if (!CreatePipe(&hStdoutReadTmp, &hStdoutWrite,  &sa, 0)) {
        CloseHandle(hStdinRead); CloseHandle(hStdinWriteTmp); return false;
    }
    if (!CreatePipe(&hStderrReadTmp, &hStderrWrite,  &sa, 0)) {
        CloseHandle(hStdinRead);    CloseHandle(hStdinWriteTmp);
        CloseHandle(hStdoutReadTmp); CloseHandle(hStdoutWrite); return false;
    }

    // Duplicate handles as non-inheritable for our side
    HANDLE hProc = GetCurrentProcess();
    DuplicateHandle(hProc, hStdinWriteTmp,  hProc, &_hStdinWrite, 0, FALSE, DUPLICATE_SAME_ACCESS);
    DuplicateHandle(hProc, hStdoutReadTmp,  hProc, &_hStdoutRead, 0, FALSE, DUPLICATE_SAME_ACCESS);
    DuplicateHandle(hProc, hStderrReadTmp,  hProc, &_hStderrRead, 0, FALSE, DUPLICATE_SAME_ACCESS);

    CloseHandle(hStdinWriteTmp);
    CloseHandle(hStdoutReadTmp);
    CloseHandle(hStderrReadTmp);

    // Set up process
    STARTUPINFOW si = {};
    si.cb         = sizeof(STARTUPINFOW);
    si.dwFlags    = STARTF_USESTDHANDLES;
    si.hStdInput  = hStdinRead;
    si.hStdOutput = hStdoutWrite;
    si.hStdError  = hStderrWrite;

    std::wstring cmdLine = L"\"" + gdbPath + L"\" --interpreter=mi2 --quiet";
    std::vector<wchar_t> cmd(cmdLine.begin(), cmdLine.end());
    cmd.push_back(0);

    PROCESS_INFORMATION pi = {};
    BOOL ok = CreateProcessW(
        nullptr, cmd.data(),
        nullptr, nullptr,
        TRUE,                // inherit handles
        CREATE_NO_WINDOW,
        nullptr, nullptr,
        &si, &pi);

    // Close child-side handles (child owns them now)
    CloseHandle(hStdinRead);
    CloseHandle(hStdoutWrite);
    CloseHandle(hStderrWrite);

    if (!ok) return false;

    _hProcess = pi.hProcess;
    _hThread  = pi.hThread;
    _running  = true;
    _token    = 1;

    _readerThread = std::thread(&GdbSession::readLoop,      this);
    _stderrThread = std::thread(&GdbSession::stderrDrain,   this);
    return true;
}

// ── Stop GDB process ──────────────────────────────────────────────────────

void GdbSession::stop() {
    if (!_running.exchange(false)) return;

    try { sendMi("-gdb-exit"); } catch (...) {}

    if (_hProcess != INVALID_HANDLE_VALUE)
        if (WaitForSingleObject(_hProcess, 2000) == WAIT_TIMEOUT)
            TerminateProcess(_hProcess, 0);

    auto safeClose = [](HANDLE& h) {
        if (h != INVALID_HANDLE_VALUE) { CloseHandle(h); h = INVALID_HANDLE_VALUE; }
    };
    safeClose(_hStdinWrite);
    safeClose(_hStdoutRead);
    safeClose(_hStderrRead);

    if (_readerThread.joinable()) _readerThread.join();
    if (_stderrThread.joinable()) _stderrThread.join();

    safeClose(_hProcess);
    safeClose(_hThread);
}

// ── MI command send ───────────────────────────────────────────────────────

int GdbSession::sendMi(const std::string& command) {
    int tok = _token.fetch_add(1);
    std::string line = std::to_string(tok) + command + "\n";
    DWORD written = 0;
    std::lock_guard<std::mutex> lk(_writeMutex);
    WriteFile(_hStdinWrite, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
    return tok;
}

// ── stdout read loop (background thread) ─────────────────────────────────

void GdbSession::readLoop() {
    std::string buf;
    buf.reserve(1024);
    char ch;
    DWORD read = 0;

    while (_running.load()) {
        BOOL ok = ReadFile(_hStdoutRead, &ch, 1, &read, nullptr);
        if (!ok || read == 0) break;

        if (ch == '\n') {
            if (!buf.empty() && buf.back() == '\r') buf.pop_back();
            GdbMi::MiLine parsed = GdbMi::parseLine(buf);
            dispatchLine(parsed);
            buf.clear();
        } else {
            buf += ch;
        }
    }

    DWORD code = 0;
    if (_hProcess != INVALID_HANDLE_VALUE)
        GetExitCodeProcess(_hProcess, &code);

    _running = false;
    if (onExited) onExited(static_cast<int>(code));
}

// ── stderr drain (background thread) ─────────────────────────────────────
// GDB's stderr pipe must be drained continuously. If the pipe buffer (~64KB)
// fills up, GDB blocks and the entire debug session freezes.

void GdbSession::stderrDrain() {
    char buf[256];
    DWORD read = 0;
    while (_running.load()) {
        BOOL ok = ReadFile(_hStderrRead, buf, sizeof(buf), &read, nullptr);
        if (!ok || read == 0) break;
        // Discard stderr output. Errors appear in MI as ^error records.
    }
}

// ── Dispatch parsed line ──────────────────────────────────────────────────

void GdbSession::dispatchLine(const GdbMi::MiLine& line) {
    if (line.result) {
        if (onResult) onResult(*line.result);
    }
    if (line.async) {
        if (line.async->cls == GdbMi::AsyncClass::Exec &&
            line.async->type == "stopped") {
            if (onStopped) onStopped(extractStop(*line.async));
        }
    }
    if (line.stream && line.stream->channel == '~') {
        if (onConsole) onConsole(line.stream->text);
    }
}

// ── Extract stop info ─────────────────────────────────────────────────────

StopInfo GdbSession::extractStop(const GdbMi::AsyncRecord& rec) {
    StopInfo info;
    info.reason = rec.results.getString("reason");

    const GdbMi::Value* frame = rec.results.get("frame");
    if (frame) {
        info.fileName = frame->getString("fullname");
        if (info.fileName.empty())
            info.fileName = frame->getString("file");
        try { info.line = std::stoi(frame->getString("line", "0")); } catch (...) {}
        info.function = frame->getString("func");
        info.address  = frame->getString("addr");
    }

    try { info.bpNumber = std::stoi(rec.results.getString("bkptno",    "0")); } catch (...) {}
    try { info.exitCode = std::stoi(rec.results.getString("exit-code", "0")); } catch (...) {}
    return info;
}

// ── Convenience wrappers ──────────────────────────────────────────────────

void GdbSession::loadExe(const std::string& path, const std::string& workDir) {
    sendMi("-file-exec-and-symbols \"" + toGdbPath(path) + "\"");
    if (!workDir.empty()) {
        std::string d = toGdbPath(workDir);
        sendMi("-environment-cd \"" + d + "\"");
        sendMi("-environment-directory \"" + d + "\"");
    }
}

void GdbSession::execRun()       { sendMi("-exec-run"); }
void GdbSession::execContinue()  { sendMi("-exec-continue"); }
void GdbSession::execNext()      { sendMi("-exec-next"); }
void GdbSession::execStep()      { sendMi("-exec-step"); }
void GdbSession::execFinish()    { sendMi("-exec-finish"); }
void GdbSession::execInterrupt() { sendMi("-exec-interrupt"); }

void GdbSession::execUntil(const std::string& file, int line) {
    sendMi("-exec-until " + file + ":" + std::to_string(line));
}

int GdbSession::breakInsert(const std::string& file, int line) {
    return sendMi("-break-insert " + file + ":" + std::to_string(line));
}

void GdbSession::breakDelete(int id)  { sendMi("-break-delete "  + std::to_string(id)); }
void GdbSession::breakEnable(int id)  { sendMi("-break-enable "  + std::to_string(id)); }
void GdbSession::breakDisable(int id) { sendMi("-break-disable " + std::to_string(id)); }

void GdbSession::breakCondition(int id, const std::string& cond) {
    sendMi("-break-condition " + std::to_string(id) + " " + cond);
}

int GdbSession::dataEvaluate(const std::string& expr) {
    return sendMi("-data-evaluate-expression \"" + expr + "\"");
}

void GdbSession::stackListFrames()    { sendMi("-stack-list-frames"); }
void GdbSession::listRegisterNames()  { sendMi("-data-list-register-names"); }
void GdbSession::listRegisterValues() { sendMi("-data-list-register-values x"); }

void GdbSession::envDirectory(const std::string& dir) {
    sendMi("-environment-directory \"" + toGdbPath(dir) + "\"");
}

void GdbSession::envCd(const std::string& dir) {
    sendMi("-environment-cd \"" + toGdbPath(dir) + "\"");
}

} // namespace GdbNative
