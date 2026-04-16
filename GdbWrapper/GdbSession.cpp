// GdbSession.cpp -- C++/CLI managed wrapper (compile with /clr:netcore)
//
// Role:
//   Wraps native GdbNative::GdbSession, exposes .NET events and types.
//   C# uFPCEditor uses only this class and does not touch GDB/MI directly.

#include "GdbSessionNative.h"

#using <System.dll>
#using <System.Runtime.dll>

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Threading;
using namespace System::Collections::Generic;

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
    property String^ ResultClass;   // "done" | "running" | "error" | ...
    property String^ Message;       // populated when ResultClass == "error"
};

// ── Delegates ────────────────────────────────────────────────────────────

public delegate void StoppedDelegate (StopInfo^     info);
public delegate void ConsoleDelegate (String^       text);
public delegate void ExitedDelegate  (int           exitCode);
public delegate void ResultDelegate  (MiResultInfo^ result);

// ── GdbSession managed class ─────────────────────────────────────────────

public ref class GdbSession : IDisposable {
public:

    // ── Events ──────────────────────────────────────────────────────────
    event StoppedDelegate^ Stopped;
    event ConsoleDelegate^ ConsoleOutput;
    event ExitedDelegate^  Exited;
    event ResultDelegate^  ResultReceived;

    // ── Properties ──────────────────────────────────────────────────────
    property String^ GdbPath;
    property bool    IsRunning {
        bool get() { return _native != nullptr && _native->isRunning(); }
    }

    // ── Constructor / Destructor ─────────────────────────────────────────
    GdbSession() : _native(nullptr), _disposed(false), _rawHandle(nullptr) {}

    ~GdbSession() {
        if (!_disposed) {
            _disposed = true;
            this->!GdbSession();
        }
    }

    !GdbSession() {
        if (_native) {
            _native->stop();
            delete _native;
            _native = nullptr;
        }
        FreeHandle();
    }

    // ── Session start ────────────────────────────────────────────────────
    void Start() {
        if (_native) { _native->stop(); delete _native; _native = nullptr; }
        FreeHandle();

        _native = new GdbNative::GdbSession();

        // Store GCHandle as void* so native lambdas (std::function) can capture it.
        // Managed value types like IntPtr cannot be directly captured in native lambdas.
        GCHandle h = GCHandle::Alloc(this);
        _rawHandle = GCHandle::ToIntPtr(h).ToPointer();

        void* rh = _rawHandle;  // local copy for lambda capture

        _native->onStopped = [rh](const GdbNative::StopInfo& info) {
            GCHandle gcH = GCHandle::FromIntPtr(IntPtr(rh));
            GdbSession^ self = safe_cast<GdbSession^>(gcH.Target);
            if (self == nullptr) return;

            auto si = gcnew StopInfo();
            si->FileName  = Utf8ToManaged(info.fileName);
            si->Line      = info.line;
            si->Function  = Utf8ToManaged(info.function);
            si->Address   = Utf8ToManaged(info.address);
            si->BreakpointNumber = info.bpNumber;
            si->ExitCode  = info.exitCode;

            const std::string& r = info.reason;
            if      (r == "breakpoint-hit")     si->Reason = StopReason::BreakpointHit;
            else if (r == "end-stepping-range"
                  || r == "function-finished")  si->Reason = StopReason::StepComplete;
            else if (r == "exited-normally")    si->Reason = StopReason::ExitedNormally;
            else if (r == "exited-signalled")   si->Reason = StopReason::ExitedSignalled;
            else if (r == "signal-received")    si->Reason = StopReason::SignalReceived;
            else                                si->Reason = StopReason::Unknown;

            self->Stopped(si);
        };

        _native->onConsole = [rh](const std::string& text) {
            GCHandle gcH = GCHandle::FromIntPtr(IntPtr(rh));
            GdbSession^ self = safe_cast<GdbSession^>(gcH.Target);
            if (self) self->ConsoleOutput(Utf8ToManaged(text));
        };

        _native->onExited = [rh](int code) {
            GCHandle gcH = GCHandle::FromIntPtr(IntPtr(rh));
            GdbSession^ self = safe_cast<GdbSession^>(gcH.Target);
            if (self) self->Exited(code);
        };

        _native->onResult = [rh](const GdbMi::ResultRecord& rec) {
            GCHandle gcH = GCHandle::FromIntPtr(IntPtr(rh));
            GdbSession^ self = safe_cast<GdbSession^>(gcH.Target);
            if (self == nullptr) return;

            auto r = gcnew MiResultInfo();
            r->Token = rec.token;
            switch (rec.cls) {
                case GdbMi::ResultClass::Done:      r->ResultClass = "done";      break;
                case GdbMi::ResultClass::Running:   r->ResultClass = "running";   break;
                case GdbMi::ResultClass::Connected: r->ResultClass = "connected"; break;
                case GdbMi::ResultClass::Error:
                    r->ResultClass = "error";
                    r->Message = Utf8ToManaged(rec.results.getString("msg"));
                    break;
                case GdbMi::ResultClass::Exit:      r->ResultClass = "exit";      break;
            }
            self->ResultReceived(r);
        };

        std::wstring path = ManagedToWide(GdbPath);
        if (!_native->start(path))
            throw gcnew InvalidOperationException("GDB start failed: " + GdbPath);
    }

    void Stop() {
        if (_native) _native->stop();
        FreeHandle();
    }

    // ── Target loading ───────────────────────────────────────────────────
    void LoadExe(String^ exePath, String^ workDir) {
        CheckRunning();
        _native->loadExe(
            ManagedToUtf8(exePath),
            workDir != nullptr ? ManagedToUtf8(workDir) : "");
    }

    // ── Execution control ────────────────────────────────────────────────
    void Run()       { CheckRunning(); _native->execRun(); }
    void Continue()  { CheckRunning(); _native->execContinue(); }
    void StepOver()  { CheckRunning(); _native->execNext(); }
    void StepInto()  { CheckRunning(); _native->execStep(); }
    void StepOut()   { CheckRunning(); _native->execFinish(); }
    void Interrupt() { CheckRunning(); _native->execInterrupt(); }
    void Until(String^ file, int line) {
        CheckRunning();
        _native->execUntil(ManagedToUtf8(file), line);
    }

    // ── Breakpoints ──────────────────────────────────────────────────────
    int  InsertBreakpoint(String^ file, int line) {
        CheckRunning();
        return _native->breakInsert(ManagedToUtf8(file), line);
    }
    void DeleteBreakpoint(int id)   { CheckRunning(); _native->breakDelete(id); }
    void EnableBreakpoint(int id)   { CheckRunning(); _native->breakEnable(id); }
    void DisableBreakpoint(int id)  { CheckRunning(); _native->breakDisable(id); }
    void SetCondition(int id, String^ cond) {
        CheckRunning();
        _native->breakCondition(id, ManagedToUtf8(cond));
    }

    // ── Data evaluation (async: result via ResultReceived event) ─────────
    int Evaluate(String^ expression) {
        CheckRunning();
        return _native->dataEvaluate(ManagedToUtf8(expression));
    }

    // ── Misc ─────────────────────────────────────────────────────────────
    void StackListFrames()    { CheckRunning(); _native->stackListFrames(); }
    void ListRegisterNames()  { CheckRunning(); _native->listRegisterNames(); }
    void ListRegisterValues() { CheckRunning(); _native->listRegisterValues(); }
    void SetSourceDir(String^ dir) {
        CheckRunning();
        _native->envDirectory(ManagedToUtf8(dir));
    }

    // ── Raw MI command (advanced) ────────────────────────────────────────
    int SendMi(String^ command) {
        CheckRunning();
        return _native->sendMi(ManagedToUtf8(command));
    }

private:
    GdbNative::GdbSession* _native;
    bool   _disposed;
    void*  _rawHandle;   // GCHandle stored as void* for native lambda capture

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

    // ── String conversion helpers ────────────────────────────────────────

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
};

} // namespace GdbWrapper
