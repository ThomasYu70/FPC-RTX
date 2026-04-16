// GdbSession.cpp — C++/CLI 관리 래퍼 (컴파일: /clr:netcore)
//
// 역할:
//   네이티브 GdbNative::GdbSession 을 감싸서 .NET 이벤트·타입으로 노출합니다.
//   C# uFPCEditor 는 이 클래스만 사용하며, GDB/MI 프로토콜을 직접 다루지 않습니다.

#include "GdbSessionNative.h"

#using <System.dll>
#using <System.Runtime.dll>

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Threading;
using namespace System::Collections::Generic;

namespace GdbWrapper {

// ── 공개 데이터 타입 ─────────────────────────────────────────────────────

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
    property String^ ResultClass;   // "done" | "running" | "error" | …
    property String^ Message;       // error 시 메시지
};

// ── 델리게이트 ────────────────────────────────────────────────────────────

public delegate void StoppedDelegate (StopInfo^      info);
public delegate void ConsoleDelegate (String^        text);
public delegate void ExitedDelegate  (int            exitCode);
public delegate void ResultDelegate  (MiResultInfo^  result);

// ── GdbSession 관리 클래스 ────────────────────────────────────────────────

public ref class GdbSession : IDisposable {
public:

    // ── 이벤트 ──────────────────────────────────────────────────────────
    event StoppedDelegate^ Stopped;
    event ConsoleDelegate^ ConsoleOutput;
    event ExitedDelegate^  Exited;
    event ResultDelegate^  ResultReceived;

    // ── 속성 ────────────────────────────────────────────────────────────
    property String^ GdbPath;
    property bool    IsRunning {
        bool get() { return _native != nullptr && _native->isRunning(); }
    }

    // ── 생성자 / 소멸자 ─────────────────────────────────────────────────
    GdbSession() : _native(nullptr), _disposed(false) {}

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
    }

    // ── 세션 시작 ────────────────────────────────────────────────────────
    void Start() {
        if (_native) { _native->stop(); delete _native; _native = nullptr; }

        _native = new GdbNative::GdbSession();

        // gcroot 없이 람다에서 관리 객체를 캡처하는 방법:
        // GCHandle 을 raw pointer 로 보관하고 람다에서 복원
        GCHandle selfHandle = GCHandle::Alloc(this);
        IntPtr selfPtr = GCHandle::ToIntPtr(selfHandle);
        _selfHandle = selfHandle;  // 수명 유지

        _native->onStopped = [selfPtr](const GdbNative::StopInfo& info) {
            GCHandle h = GCHandle::FromIntPtr(selfPtr);
            GdbSession^ self = safe_cast<GdbSession^>(h.Target);
            if (self == nullptr) return;

            auto si       = gcnew StopInfo();
            si->FileName  = NativeToManaged(info.fileName);
            si->Line      = info.line;
            si->Function  = NativeToManaged(info.function);
            si->Address   = NativeToManaged(info.address);
            si->BreakpointNumber = info.bpNumber;
            si->ExitCode  = info.exitCode;

            const std::string& r = info.reason;
            if      (r == "breakpoint-hit")                       si->Reason = StopReason::BreakpointHit;
            else if (r == "end-stepping-range" || r == "function-finished") si->Reason = StopReason::StepComplete;
            else if (r == "exited-normally")                       si->Reason = StopReason::ExitedNormally;
            else if (r == "exited-signalled")                      si->Reason = StopReason::ExitedSignalled;
            else if (r == "signal-received")                       si->Reason = StopReason::SignalReceived;
            else                                                   si->Reason = StopReason::Unknown;

            self->Stopped(si);
        };

        _native->onConsole = [selfPtr](const std::string& text) {
            GCHandle h = GCHandle::FromIntPtr(selfPtr);
            GdbSession^ self = safe_cast<GdbSession^>(h.Target);
            if (self) self->ConsoleOutput(NativeToManaged(text));
        };

        _native->onExited = [selfPtr](int code) {
            GCHandle h = GCHandle::FromIntPtr(selfPtr);
            GdbSession^ self = safe_cast<GdbSession^>(h.Target);
            if (self) {
                self->Exited(code);
                h.Free();   // 세션 종료 시 GCHandle 해제
            }
        };

        _native->onResult = [selfPtr](const GdbMi::ResultRecord& rec) {
            GCHandle h = GCHandle::FromIntPtr(selfPtr);
            GdbSession^ self = safe_cast<GdbSession^>(h.Target);
            if (self == nullptr) return;

            auto r       = gcnew MiResultInfo();
            r->Token     = rec.token;
            switch (rec.cls) {
                case GdbMi::ResultClass::Done:      r->ResultClass = "done";      break;
                case GdbMi::ResultClass::Running:   r->ResultClass = "running";   break;
                case GdbMi::ResultClass::Connected: r->ResultClass = "connected"; break;
                case GdbMi::ResultClass::Error:
                    r->ResultClass = "error";
                    r->Message = NativeToManaged(rec.results.getString("msg"));
                    break;
                case GdbMi::ResultClass::Exit:      r->ResultClass = "exit";      break;
            }
            self->ResultReceived(r);
        };

        std::wstring path = ManagedToWide(GdbPath);
        if (!_native->start(path))
            throw gcnew InvalidOperationException("GDB 시작 실패: " + GdbPath);
    }

    void Stop() {
        if (_native) _native->stop();
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    // ── 타깃 로드 ────────────────────────────────────────────────────────
    void LoadExe(String^ exePath, String^ workDir) {
        CheckRunning();
        _native->loadExe(ManagedToUtf8(exePath),
                         workDir != nullptr ? ManagedToUtf8(workDir) : "");
    }

    // ── 실행 제어 ────────────────────────────────────────────────────────
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

    // ── 브레이크포인트 ───────────────────────────────────────────────────
    // 토큰을 반환 → ResultReceived 이벤트에서 같은 Token 으로 결과 수신
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

    // ── 데이터 평가 ─────────────────────────────────────────────────────
    // 반환값: 토큰 → ResultReceived 이벤트에서 결과 수신
    int Evaluate(String^ expression) {
        CheckRunning();
        return _native->dataEvaluate(ManagedToUtf8(expression));
    }

    // ── 기타 ─────────────────────────────────────────────────────────────
    void StackListFrames()    { CheckRunning(); _native->stackListFrames(); }
    void ListRegisterNames()  { CheckRunning(); _native->listRegisterNames(); }
    void ListRegisterValues() { CheckRunning(); _native->listRegisterValues(); }
    void SetSourceDir(String^ dir) {
        CheckRunning();
        _native->envDirectory(ManagedToUtf8(dir));
    }

    // ── 로우 MI 명령 (고급 사용) ─────────────────────────────────────────
    int SendMi(String^ command) {
        CheckRunning();
        return _native->sendMi(ManagedToUtf8(command));
    }

private:
    GdbNative::GdbSession* _native;
    bool    _disposed;
    GCHandle _selfHandle;

    void CheckRunning() {
        if (_native == nullptr || !_native->isRunning())
            throw gcnew InvalidOperationException("GDB 세션이 실행 중이 아닙니다.");
    }

    // ── 문자열 변환 헬퍼 ────────────────────────────────────────────────

    static String^ NativeToManaged(const std::string& s) {
        if (s.empty()) return String::Empty;
        array<Byte>^ bytes = gcnew array<Byte>((int)s.size());
        for (int i = 0; i < (int)s.size(); i++) bytes[i] = (Byte)(unsigned char)s[i];
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
