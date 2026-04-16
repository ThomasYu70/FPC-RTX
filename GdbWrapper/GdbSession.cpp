// GdbSession.cpp -- C++/CLI managed wrapper (/clr:netcore)
//
// C++/CLI restriction: lambdas (local classes) are not allowed inside
// ref class member functions (C3923).
// Solution: static file-scope callbacks + std::bind.

#include "GdbSessionNative.h"
#include <vcclr.h>         // PtrToStringChars
#include <functional>      // std::bind, std::placeholders

#using <System.dll>
#using <System.Runtime.dll>

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;

namespace GdbWrapper {

// ── Public data types ─────────────────────────────────────────────────────

public enum class StopReason {
    Unknown,
    BreakpointHit,
    StepComplete,
    ExitedNormally,
    ExitedSignalled,
    SignalReceived
};

public ref class StopInfo {
public:
    property StopReason Reason;
    property String^    FileName;
    property int        Line;
    property String^    Function;
    property String^    Address;
    property int        BreakpointNumber;
    property int        ExitCode;
};

public ref class MiResultInfo {
public:
    property int     Token;
    property String^ ResultClass;
    property String^ Message;
};

public delegate void StoppedDelegate (StopInfo^     info);
public delegate void ConsoleDelegate (String^       text);
public delegate void ExitedDelegate  (int           exitCode);
public delegate void ResultDelegate  (MiResultInfo^ result);

// ── Forward declaration of ref class ─────────────────────────────────────

ref class GdbSession;

// ── String helpers (file scope, usable from static callbacks) ────────────

static String^ Utf8ToManaged(const std::string& s) {
    if (s.empty()) return String::Empty;
    array<Byte>^ bytes = gcnew array<Byte>((int)s.size());
    for (int i = 0; i < (int)s.size(); i++)
        bytes[i] = static_cast<Byte>(static_cast<unsigned char>(s[i]));
    return Encoding::UTF8->GetString(bytes);
}

static std::wstring ManagedToWide(String^ s) {
    if (s == nullptr || s->Length == 0) return L"";
    pin_ptr<const wchar_t> p = PtrToStringChars(s);
    return std::wstring(p, s->Length);
}

static std::string ManagedToUtf8(String^ s) {
    if (s == nullptr || s->Length == 0) return "";
    array<Byte>^ bytes = Encoding::UTF8->GetBytes(s);
    if (bytes->Length == 0) return "";
    pin_ptr<Byte> p = &bytes[0];
    return std::string(reinterpret_cast<char*>(p), bytes->Length);
}

// ── GdbSession ref class ─────────────────────────────────────────────────

public ref class GdbSession : IDisposable {
public:
    event StoppedDelegate^ Stopped;
    event ConsoleDelegate^ ConsoleOutput;
    event ExitedDelegate^  Exited;
    event ResultDelegate^  ResultReceived;

    property String^ GdbPath;
    property bool    IsRunning {
        bool get() { return _native != nullptr && _native->isRunning(); }
    }

    GdbSession() : _native(nullptr), _disposed(false), _rawHandle(nullptr) {}

    ~GdbSession() {
        if (!_disposed) { _disposed = true; this->!GdbSession(); }
    }
    !GdbSession() {
        if (_native) { _native->stop(); delete _native; _native = nullptr; }
        FreeHandle();
    }

    // ── internal raise methods (called by file-scope static callbacks) ───
    // Event can only be raised from inside the declaring class.
internal:
    void RaiseStopped(StopInfo^ si)    { Stopped(si); }
    void RaiseConsole(String^ text)    { ConsoleOutput(text); }
    void RaiseExited(int code)         { Exited(code); }
    void RaiseResult(MiResultInfo^ r)  { ResultReceived(r); }

public:
    // ── Session start ────────────────────────────────────────────────────
    void Start();   // defined after static callbacks below

    void Stop() { if (_native) _native->stop(); FreeHandle(); }

    // ── Target ───────────────────────────────────────────────────────────
    void LoadExe(String^ exePath, String^ workDir) {
        CheckRunning();
        _native->loadExe(ManagedToUtf8(exePath),
                         workDir != nullptr ? ManagedToUtf8(workDir) : "");
    }

    // ── Execution ────────────────────────────────────────────────────────
    void Run()       { CheckRunning(); _native->execRun(); }
    void Continue()  { CheckRunning(); _native->execContinue(); }
    void StepOver()  { CheckRunning(); _native->execNext(); }
    void StepInto()  { CheckRunning(); _native->execStep(); }
    void StepOut()   { CheckRunning(); _native->execFinish(); }
    void Interrupt() { CheckRunning(); _native->execInterrupt(); }
    void Until(String^ file, int line) {
        CheckRunning(); _native->execUntil(ManagedToUtf8(file), line);
    }

    // ── Breakpoints ──────────────────────────────────────────────────────
    int  InsertBreakpoint(String^ file, int line) {
        CheckRunning(); return _native->breakInsert(ManagedToUtf8(file), line);
    }
    void DeleteBreakpoint(int id)   { CheckRunning(); _native->breakDelete(id); }
    void EnableBreakpoint(int id)   { CheckRunning(); _native->breakEnable(id); }
    void DisableBreakpoint(int id)  { CheckRunning(); _native->breakDisable(id); }
    void SetCondition(int id, String^ cond) {
        CheckRunning(); _native->breakCondition(id, ManagedToUtf8(cond));
    }

    // ── Data ─────────────────────────────────────────────────────────────
    int Evaluate(String^ expr) {
        CheckRunning(); return _native->dataEvaluate(ManagedToUtf8(expr));
    }
    void StackListFrames()    { CheckRunning(); _native->stackListFrames(); }
    void ListRegisterNames()  { CheckRunning(); _native->listRegisterNames(); }
    void ListRegisterValues() { CheckRunning(); _native->listRegisterValues(); }
    void SetSourceDir(String^ dir) {
        CheckRunning(); _native->envDirectory(ManagedToUtf8(dir));
    }

    // ── Raw MI ───────────────────────────────────────────────────────────
    int SendMi(String^ command) {
        CheckRunning(); return _native->sendMi(ManagedToUtf8(command));
    }

private:
    GdbNative::GdbSession* _native;
    bool   _disposed;
    void*  _rawHandle;   // GCHandle as void* for use outside ref class

    void FreeHandle() {
        if (_rawHandle != nullptr) {
            GCHandle h = GCHandle::FromIntPtr(IntPtr(_rawHandle));
            if (h.IsAllocated) h.Free();
            _rawHandle = nullptr;
        }
    }

    void CheckRunning() {
        if (_native == nullptr || !_native->isRunning())
            throw gcnew InvalidOperationException("GDB session is not running.");
    }
};

// ── File-scope static callbacks ───────────────────────────────────────────
// Defined OUTSIDE ref class to avoid C3923 (no lambdas in ref class methods).
// Compiled with /clr:netcore so gcnew / GCHandle are available.

static void CB_Stopped(void* rh, const GdbNative::StopInfo& info) {
    GCHandle h = GCHandle::FromIntPtr(IntPtr(rh));
    GdbSession^ self = safe_cast<GdbSession^>(h.Target);
    if (self == nullptr) return;

    StopInfo^ si = gcnew StopInfo();
    si->FileName         = Utf8ToManaged(info.fileName);
    si->Line             = info.line;
    si->Function         = Utf8ToManaged(info.function);
    si->Address          = Utf8ToManaged(info.address);
    si->BreakpointNumber = info.bpNumber;
    si->ExitCode         = info.exitCode;

    const std::string& r = info.reason;
    if      (r == "breakpoint-hit")    si->Reason = StopReason::BreakpointHit;
    else if (r == "end-stepping-range"
          || r == "function-finished") si->Reason = StopReason::StepComplete;
    else if (r == "exited-normally")   si->Reason = StopReason::ExitedNormally;
    else if (r == "exited-signalled")  si->Reason = StopReason::ExitedSignalled;
    else if (r == "signal-received")   si->Reason = StopReason::SignalReceived;
    else                               si->Reason = StopReason::Unknown;

    self->RaiseStopped(si);
}

static void CB_Console(void* rh, const std::string& text) {
    GCHandle h = GCHandle::FromIntPtr(IntPtr(rh));
    GdbSession^ self = safe_cast<GdbSession^>(h.Target);
    if (self) self->RaiseConsole(Utf8ToManaged(text));
}

static void CB_Exited(void* rh, int code) {
    GCHandle h = GCHandle::FromIntPtr(IntPtr(rh));
    GdbSession^ self = safe_cast<GdbSession^>(h.Target);
    if (self) self->RaiseExited(code);
}

static void CB_Result(void* rh, const GdbMi::ResultRecord& rec) {
    GCHandle h = GCHandle::FromIntPtr(IntPtr(rh));
    GdbSession^ self = safe_cast<GdbSession^>(h.Target);
    if (self == nullptr) return;

    MiResultInfo^ r = gcnew MiResultInfo();
    r->Token = rec.token;
    switch (rec.cls) {
        case GdbMi::ResultClass::Done:      r->ResultClass = "done";      break;
        case GdbMi::ResultClass::Running:   r->ResultClass = "running";   break;
        case GdbMi::ResultClass::Connected: r->ResultClass = "connected"; break;
        case GdbMi::ResultClass::Error:
            r->ResultClass = "error";
            r->Message     = Utf8ToManaged(rec.results.getString("msg"));
            break;
        case GdbMi::ResultClass::Exit:      r->ResultClass = "exit";      break;
    }
    self->RaiseResult(r);
}

// ── GdbSession::Start (defined here, after static callbacks) ─────────────

void GdbSession::Start() {
    if (_native) { _native->stop(); delete _native; _native = nullptr; }
    FreeHandle();

    _native = new GdbNative::GdbSession();

    // Store GCHandle as void* so it can be passed to static callbacks.
    GCHandle h = GCHandle::Alloc(this);
    _rawHandle = GCHandle::ToIntPtr(h).ToPointer();
    void* rh   = _rawHandle;

    // std::bind avoids lambda definitions in this ref class method.
    using namespace std::placeholders;
    _native->onStopped = std::bind(&CB_Stopped, rh, _1);
    _native->onConsole = std::bind(&CB_Console, rh, _1);
    _native->onExited  = std::bind(&CB_Exited,  rh, _1);
    _native->onResult  = std::bind(&CB_Result,  rh, _1);

    std::wstring path = ManagedToWide(GdbPath);
    if (!_native->start(path))
        throw gcnew InvalidOperationException("GDB start failed: " + GdbPath);
}

} // namespace GdbWrapper
