#include "pch.h"
#include "CompilerBridge.h"

using namespace System;
using namespace System::IO;
using namespace System::Reflection;
using namespace System::Text;
using namespace System::Runtime::InteropServices;

namespace
{
    [StructLayout(LayoutKind::Sequential, Pack = 1)]
    public value struct NativeRunResult
    {
        UInt32 StructSize;
        Int32 ApiStatus;
        Int32 CompilerExitCode;
        UInt32 OutputChars;
    };

    [StructLayout(LayoutKind::Sequential, Pack = 1)]
    public value struct NativeDebugArchitecture
    {
        UInt32 StructSize;
        UInt32 Backend;
        UInt32 DebugFormat;
        UInt32 Capabilities;
    };

    public ref class NativeMethods abstract sealed
    {
    public:
        [DllImport("ufpcbridge.dll", CharSet = CharSet::Unicode)]
        static Int32 uFPC_RunCompiler(
            String^ compilerPath,
            String^ workingDirectory,
            String^ commandLine,
            NativeRunResult% resultInfo);

        [DllImport("ufpcbridge.dll", CharSet = CharSet::Unicode)]
        static Int32 uFPC_CreateHostExecutable(
            String^ runtimeImagePath,
            String^ executablePath);

        [DllImport("ufpcbridge.dll", CharSet = CharSet::Unicode)]
        static UInt32 uFPC_CopyLastOutput(IntPtr buffer, UInt32 bufferChars);

        [DllImport("ufpcbridge.dll", CharSet = CharSet::Unicode)]
        static UInt32 uFPC_CopyLastError(IntPtr buffer, UInt32 bufferChars);

        [DllImport("ufpcbridge.dll", CharSet = CharSet::Unicode)]
        static Int32 uFPC_GetDebugArchitecture(NativeDebugArchitecture% sessionInfo);
    };

    String^ SafeText(String^ value)
    {
        return value == nullptr ? String::Empty : value;
    }

    String^ GetManagedLogPath()
    {
        return Path::Combine(AppDomain::CurrentDomain->BaseDirectory, "uFPCManaged.exception.log");
    }

    void AppendManagedLog(String^ operation, String^ details)
    {
        String^ entry = String::Format(
            "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}",
            DateTime::Now,
            SafeText(operation),
            SafeText(details),
            Environment::NewLine);

        File::AppendAllText(GetManagedLogPath(), entry, Encoding::UTF8);
    }

    void LogManagedFailure(String^ operation, Int32 apiStatus, String^ details, String^ errorText)
    {
        if (apiStatus == 0)
            return;

        AppendManagedLog(
            operation,
            String::Format(
                "ApiStatus={0} | Details={1} | Error={2}",
                apiStatus,
                SafeText(details),
                SafeText(errorText)));
    }

    void LogManagedException(String^ operation, Exception^ exception, String^ details)
    {
        AppendManagedLog(
            operation,
            String::Format(
                "Details={0}{1}{2}",
                SafeText(details),
                Environment::NewLine,
                exception == nullptr ? String::Empty : exception->ToString()));
    }

    String^ CopyWideBuffer(Func<IntPtr, UInt32, UInt32>^ copier)
    {
        UInt32 required = copier(IntPtr::Zero, 0);
        if (required == 0)
            return String::Empty;

        IntPtr buffer = Marshal::AllocHGlobal(static_cast<int>(required * sizeof(wchar_t)));
        try
        {
            copier(buffer, required);
            String^ value = Marshal::PtrToStringUni(buffer);
            return value == nullptr ? String::Empty : value;
        }
        finally
        {
            Marshal::FreeHGlobal(buffer);
        }
    }
}

namespace uFPCManaged
{
    CompilerRunResult CompilerBridge::RunCompiler(String^ compilerPath, String^ workingDirectory, String^ commandLine)
    {
        try
        {
            NativeRunResult nativeResult{};
            nativeResult.StructSize = sizeof(NativeRunResult);

            Int32 apiStatus = NativeMethods::uFPC_RunCompiler(
                compilerPath,
                workingDirectory,
                commandLine,
                nativeResult);

            CompilerRunResult result;
            result.ApiStatus = apiStatus;
            result.CompilerExitCode = nativeResult.CompilerExitCode;
            result.Output = CopyWideBuffer(gcnew Func<IntPtr, UInt32, UInt32>(&NativeMethods::uFPC_CopyLastOutput));
            result.Error = CopyWideBuffer(gcnew Func<IntPtr, UInt32, UInt32>(&NativeMethods::uFPC_CopyLastError));

            LogManagedFailure(
                "RunCompiler",
                apiStatus,
                String::Format(
                    "CompilerPath={0} | WorkingDirectory={1} | CommandLine={2}",
                    SafeText(compilerPath),
                    SafeText(workingDirectory),
                    SafeText(commandLine)),
                result.Error);
            return result;
        }
        catch (Exception^ exception)
        {
            LogManagedException(
                "RunCompiler",
                exception,
                String::Format(
                    "CompilerPath={0} | WorkingDirectory={1} | CommandLine={2}",
                    SafeText(compilerPath),
                    SafeText(workingDirectory),
                    SafeText(commandLine)));

            CompilerRunResult result;
            result.ApiStatus = static_cast<int>(uFpcApiStatus::InternalError);
            result.CompilerExitCode = -1;
            result.Output = String::Empty;
            result.Error = exception == nullptr ? "Managed wrapper failure." : exception->Message;
            return result;
        }
    }

    HostExecutableResult CompilerBridge::CreateHostExecutable(String^ runtimeImagePath, String^ executablePath)
    {
        try
        {
            Int32 apiStatus = NativeMethods::uFPC_CreateHostExecutable(
                runtimeImagePath,
                executablePath);

            HostExecutableResult result;
            result.ApiStatus = apiStatus;
            result.ExecutablePath = CopyWideBuffer(gcnew Func<IntPtr, UInt32, UInt32>(&NativeMethods::uFPC_CopyLastOutput));
            result.Error = CopyWideBuffer(gcnew Func<IntPtr, UInt32, UInt32>(&NativeMethods::uFPC_CopyLastError));

            LogManagedFailure(
                "CreateHostExecutable",
                apiStatus,
                String::Format(
                    "RuntimeImagePath={0} | ExecutablePath={1}",
                    SafeText(runtimeImagePath),
                    SafeText(executablePath)),
                result.Error);
            return result;
        }
        catch (Exception^ exception)
        {
            LogManagedException(
                "CreateHostExecutable",
                exception,
                String::Format(
                    "RuntimeImagePath={0} | ExecutablePath={1}",
                    SafeText(runtimeImagePath),
                    SafeText(executablePath)));

            HostExecutableResult result;
            result.ApiStatus = static_cast<int>(uFpcApiStatus::InternalError);
            result.ExecutablePath = String::Empty;
            result.Error = exception == nullptr ? "Managed wrapper failure." : exception->Message;
            return result;
        }
    }

    CompileHostExecutableResult CompilerBridge::CompileAndCreateHostExecutable(
        String^ compilerPath,
        String^ workingDirectory,
        String^ commandLine,
        String^ runtimeImagePath,
        String^ executablePath)
    {
        try
        {
            CompilerRunResult compilerResult = RunCompiler(compilerPath, workingDirectory, commandLine);

            CompileHostExecutableResult result;
            result.CompilerApiStatus = compilerResult.ApiStatus;
            result.CompilerExitCode = compilerResult.CompilerExitCode;
            result.CompilerOutput = compilerResult.Output;
            result.CompilerError = compilerResult.Error;
            result.HostExecutableApiStatus = static_cast<int>(uFpcApiStatus::Ok);
            result.HostExecutablePath = String::Empty;
            result.HostExecutableError = String::Empty;
            result.HostExecutableAttempted = false;

            if (compilerResult.ApiStatus != static_cast<int>(uFpcApiStatus::Ok) ||
                compilerResult.CompilerExitCode != 0)
            {
                return result;
            }

            HostExecutableResult hostExecutableResult = CreateHostExecutable(runtimeImagePath, executablePath);
            result.HostExecutableAttempted = true;
            result.HostExecutableApiStatus = hostExecutableResult.ApiStatus;
            result.HostExecutablePath = hostExecutableResult.ExecutablePath;
            result.HostExecutableError = hostExecutableResult.Error;
            return result;
        }
        catch (Exception^ exception)
        {
            LogManagedException(
                "CompileAndCreateHostExecutable",
                exception,
                String::Format(
                    "CompilerPath={0} | WorkingDirectory={1} | RuntimeImagePath={2} | ExecutablePath={3}",
                    SafeText(compilerPath),
                    SafeText(workingDirectory),
                    SafeText(runtimeImagePath),
                    SafeText(executablePath)));

            CompileHostExecutableResult result;
            result.CompilerApiStatus = static_cast<int>(uFpcApiStatus::InternalError);
            result.CompilerExitCode = -1;
            result.CompilerOutput = String::Empty;
            result.CompilerError = exception == nullptr ? "Managed wrapper failure." : exception->Message;
            result.HostExecutableApiStatus = static_cast<int>(uFpcApiStatus::InternalError);
            result.HostExecutablePath = String::Empty;
            result.HostExecutableError = exception == nullptr ? "Managed wrapper failure." : exception->Message;
            result.HostExecutableAttempted = false;
            return result;
        }
    }

    DebugArchitectureInfo CompilerBridge::GetDebugArchitecture()
    {
        try
        {
            NativeDebugArchitecture nativeInfo{};
            nativeInfo.StructSize = sizeof(NativeDebugArchitecture);
            Int32 apiStatus = NativeMethods::uFPC_GetDebugArchitecture(nativeInfo);

            LogManagedFailure("GetDebugArchitecture", apiStatus, String::Empty, CopyWideBuffer(gcnew Func<IntPtr, UInt32, UInt32>(&NativeMethods::uFPC_CopyLastError)));

            DebugArchitectureInfo info;
            info.Backend = static_cast<uFpcDebugBackend>(nativeInfo.Backend);
            info.DebugFormat = static_cast<uFpcDebugFormat>(nativeInfo.DebugFormat);
            info.Capabilities = static_cast<uFpcDebugCapability>(nativeInfo.Capabilities);
            return info;
        }
        catch (Exception^ exception)
        {
            LogManagedException("GetDebugArchitecture", exception, String::Empty);

            DebugArchitectureInfo info;
            info.Backend = uFpcDebugBackend::None;
            info.DebugFormat = uFpcDebugFormat::None;
            info.Capabilities = uFpcDebugCapability::None;
            return info;
        }
    }
}
