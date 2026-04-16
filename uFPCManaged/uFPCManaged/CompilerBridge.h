#pragma once

using namespace System;

namespace uFPCManaged
{
    public enum class uFpcApiStatus : int
    {
        Ok = 0,
        InvalidArgument = 1,
        LaunchFailed = 2,
        InternalError = 3,
        NotImplemented = 4
    };

    public enum class uFpcDebugBackend : unsigned int
    {
        None = 0,
        GdbMi = 1
    };

    public enum class uFpcDebugFormat : unsigned int
    {
        None = 0,
        Dwarf = 1
    };

    [Flags]
    public enum class uFpcDebugCapability : unsigned int
    {
        None = 0,
        Breakpoints = 1u << 0,
        Watches = 1u << 1,
        Registers = 1u << 2,
        CallStack = 1u << 3,
        SourcePaths = 1u << 4
    };

    public value struct CompilerRunResult
    {
        int ApiStatus;
        int CompilerExitCode;
        String^ Output;
        String^ Error;
    };

    public value struct HostExecutableResult
    {
        int ApiStatus;
        String^ ExecutablePath;
        String^ Error;
    };

    public value struct CompileHostExecutableResult
    {
        int CompilerApiStatus;
        int CompilerExitCode;
        String^ CompilerOutput;
        String^ CompilerError;
        int HostExecutableApiStatus;
        String^ HostExecutablePath;
        String^ HostExecutableError;
        bool HostExecutableAttempted;
    };

    public value struct DebugArchitectureInfo
    {
        uFpcDebugBackend Backend;
        uFpcDebugFormat DebugFormat;
        uFpcDebugCapability Capabilities;
    };

    public ref class CompilerBridge abstract sealed
    {
    public:
        static CompilerRunResult RunCompiler(String^ compilerPath, String^ workingDirectory, String^ commandLine);
        static HostExecutableResult CreateHostExecutable(String^ runtimeImagePath, String^ executablePath);
        static CompileHostExecutableResult CompileAndCreateHostExecutable(
            String^ compilerPath,
            String^ workingDirectory,
            String^ commandLine,
            String^ runtimeImagePath,
            String^ executablePath);
        static DebugArchitectureInfo GetDebugArchitecture();
    };
}
