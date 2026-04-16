library ufpcbridge;

{$mode objfpc}{$H+}

uses
  Windows,
  Classes,
  SysUtils,
  Process;

const
  UFPC_API_VERSION = 1;

  UFPC_API_OK = 0;
  UFPC_API_E_INVALID_ARGUMENT = 1;
  UFPC_API_E_LAUNCH_FAILED = 2;
  UFPC_API_E_INTERNAL_ERROR = 3;
  UFPC_API_E_NOT_IMPLEMENTED = 4;

  UFPC_DEBUG_BACKEND_NONE = 0;
  UFPC_DEBUG_BACKEND_GDB_MI = 1;

  UFPC_DEBUG_FORMAT_NONE = 0;
  UFPC_DEBUG_FORMAT_DWARF = 1;

  UFPC_DEBUG_CAP_BREAKPOINTS = 1 shl 0;
  UFPC_DEBUG_CAP_WATCHES = 1 shl 1;
  UFPC_DEBUG_CAP_REGISTERS = 1 shl 2;
  UFPC_DEBUG_CAP_CALLSTACK = 1 shl 3;
  UFPC_DEBUG_CAP_SOURCE_PATHS = 1 shl 4;

type
  PuFpcRunResult = ^TuFpcRunResult;
  TuFpcRunResult = packed record
    StructSize: Cardinal;
    ApiStatus: LongInt;
    CompilerExitCode: LongInt;
    OutputChars: Cardinal;
  end;

  PuFpcDebugSessionRequest = ^TuFpcDebugSessionRequest;
  TuFpcDebugSessionRequest = packed record
    StructSize: Cardinal;
    GdbPath: PWideChar;
    ExecutablePath: PWideChar;
    WorkingDirectory: PWideChar;
    SourceDirectory: PWideChar;
    PreferredBackend: Cardinal;
  end;

  PuFpcDebugSessionInfo = ^TuFpcDebugSessionInfo;
  TuFpcDebugSessionInfo = packed record
    StructSize: Cardinal;
    Backend: Cardinal;
    DebugFormat: Cardinal;
    Capabilities: Cardinal;
  end;

  PUInt64 = ^QWord;

var
  LastOutput: UnicodeString = '';
  LastError: UnicodeString = '';

function GetBridgeLogPath: UnicodeString;
var
  buffer: array[0..MAX_PATH - 1] of WideChar;
  pathLength: DWORD;
begin
  pathLength := GetModuleFileNameW(HInstance, @buffer[0], Length(buffer));
  if pathLength = 0 then
    Exit('ufpcbridge.exception.log');

  SetString(Result, PWideChar(@buffer[0]), pathLength);
  Result := ExtractFilePath(Result) + 'ufpcbridge.exception.log';
end;

procedure AppendBridgeLog(const operation, details: UnicodeString);
var
  handle: THandle;
  entry: UTF8String;
  bytesWritten: DWORD;
  lineText: UnicodeString;
begin
  lineText := FormatDateTime('yyyy-mm-dd hh:nn:ss.zzz', Now)
           + ' [' + operation + '] '
           + details
           + UnicodeString(sLineBreak);
  entry := UTF8Encode(lineText);

  handle := CreateFileW(
    PWideChar(GetBridgeLogPath),
    FILE_APPEND_DATA,
    FILE_SHARE_READ or FILE_SHARE_WRITE,
    nil,
    OPEN_ALWAYS,
    FILE_ATTRIBUTE_NORMAL,
    0);
  if handle = INVALID_HANDLE_VALUE then
    Exit;

  try
    if Length(entry) > 0 then
      WriteFile(handle, entry[1], Length(entry), bytesWritten, nil);
  finally
    CloseHandle(handle);
  end;
end;

procedure LogBridgeFailure(const operation, details: UnicodeString);
begin
  AppendBridgeLog(operation, details);
end;

procedure ResetLastState;
begin
  LastOutput := '';
  LastError := '';
end;

function WideTextOf(value: PWideChar): UnicodeString;
begin
  if Assigned(value) then
    Result := UnicodeString(WideString(value))
  else
    Result := '';
end;

function CopyWideString(const value: UnicodeString; buffer: PWideChar; bufferChars: Cardinal): Cardinal;
var
  copyChars: Cardinal;
begin
  Result := Length(value) + 1;
  if not Assigned(buffer) or (bufferChars = 0) then
    Exit;

  copyChars := bufferChars - 1;
  if copyChars > Cardinal(Length(value)) then
    copyChars := Length(value);

  if copyChars > 0 then
    Move(PWideChar(value)^, buffer^, copyChars * SizeOf(WideChar));
  buffer[copyChars] := #0;
end;

function TryCopyFileWide(const sourcePath, targetPath: UnicodeString): Boolean;
begin
  Result := CopyFileW(PWideChar(sourcePath), PWideChar(targetPath), False);
end;

function ExpandWidePath(const path: UnicodeString): UnicodeString;
begin
  Result := UnicodeString(ExpandFileName(String(path)));
end;

function DeriveHostExecutablePath(const runtimeImagePath, executablePath: UnicodeString): UnicodeString;
begin
  if executablePath <> '' then
    Exit(executablePath);

  Result := ChangeFileExt(runtimeImagePath, '.exe');
end;

function DeriveCompanionDebugPath(const anyArtifactPath: UnicodeString): UnicodeString;
begin
  Result := ChangeFileExt(anyArtifactPath, '.dbg');
end;

function CopyCompanionDebugFile(const sourceArtifactPath, targetArtifactPath: UnicodeString): Boolean;
var
  sourceDebugPath: UnicodeString;
  targetDebugPath: UnicodeString;
begin
  sourceDebugPath := DeriveCompanionDebugPath(sourceArtifactPath);
  targetDebugPath := DeriveCompanionDebugPath(targetArtifactPath);

  if CompareText(ExpandWidePath(sourceDebugPath), ExpandWidePath(targetDebugPath)) = 0 then
    Exit(True);

  if GetFileAttributesW(PWideChar(sourceDebugPath)) = INVALID_FILE_ATTRIBUTES then
    Exit(True);

  Result := TryCopyFileWide(sourceDebugPath, targetDebugPath);
end;

function CaptureProcessOutput(processHandle: TProcess): RawByteString;
var
  bytesRead: LongInt;
  buffer: array[0..4095] of Byte;
  stream: TBytesStream;
begin
  stream := TBytesStream.Create;
  try
    while processHandle.Running or (processHandle.Output.NumBytesAvailable > 0) do
      begin
        while processHandle.Output.NumBytesAvailable > 0 do
          begin
            bytesRead := processHandle.Output.Read(buffer, SizeOf(buffer));
            if bytesRead <= 0 then
              Break;
            stream.WriteBuffer(buffer, bytesRead);
          end;
        if processHandle.Running then
          Sleep(10);
      end;

    SetLength(Result, stream.Size);
    if stream.Size > 0 then
      Move(stream.Memory^, Result[1], stream.Size);
  finally
    stream.Free;
  end;
end;

function uFPC_GetApiVersion: Cardinal; stdcall;
begin
  Result := UFPC_API_VERSION;
end;

function uFPC_RunCompiler(
  compilerPath: PWideChar;
  workingDirectory: PWideChar;
  commandLine: PWideChar;
  resultInfo: PuFpcRunResult): LongInt; stdcall;
var
  compilerPathText: UnicodeString;
  workingDirectoryText: UnicodeString;
  commandLineText: UnicodeString;
  processHandle: TProcess;
  outputBytes: RawByteString;
begin
  ResetLastState;

  if not Assigned(resultInfo) or
     (resultInfo^.StructSize < SizeOf(TuFpcRunResult)) then
    Exit(UFPC_API_E_INVALID_ARGUMENT);

  FillChar(resultInfo^, SizeOf(TuFpcRunResult), 0);
  resultInfo^.StructSize := SizeOf(TuFpcRunResult);

  compilerPathText := WideTextOf(compilerPath);
  workingDirectoryText := WideTextOf(workingDirectory);
  commandLineText := WideTextOf(commandLine);

  if compilerPathText = '' then
    begin
      LastError := 'uFPC_RunCompiler requires a compiler path.';
      LogBridgeFailure('uFPC_RunCompiler', LastError);
      resultInfo^.ApiStatus := UFPC_API_E_INVALID_ARGUMENT;
      Exit(UFPC_API_E_INVALID_ARGUMENT);
    end;

  processHandle := TProcess.Create(nil);
  try
    processHandle.Options := [poUsePipes, poStderrToOutPut, poNoConsole];
    processHandle.CommandLine := '"' + compilerPathText + '" ' + commandLineText;
    if workingDirectoryText <> '' then
      processHandle.CurrentDirectory := workingDirectoryText;

    try
      processHandle.Execute;
    except
      on E: Exception do
        begin
          LastError := 'Failed to launch compiler: ' + UnicodeString(E.Message);
          LogBridgeFailure(
            'uFPC_RunCompiler',
            LastError
            + ' | CompilerPath=' + compilerPathText
            + ' | WorkingDirectory=' + workingDirectoryText
            + ' | CommandLine=' + commandLineText
            + ' | ExceptionClass=' + UnicodeString(E.ClassName));
          resultInfo^.ApiStatus := UFPC_API_E_LAUNCH_FAILED;
          Exit(UFPC_API_E_LAUNCH_FAILED);
        end;
    end;

    outputBytes := CaptureProcessOutput(processHandle);
    processHandle.WaitOnExit;

    LastOutput := UnicodeString(String(outputBytes));
    resultInfo^.ApiStatus := UFPC_API_OK;
    resultInfo^.CompilerExitCode := processHandle.ExitStatus;
    resultInfo^.OutputChars := Length(LastOutput);
    Result := UFPC_API_OK;
  except
    on E: Exception do
      begin
        LastError := 'uFPC bridge internal error: ' + UnicodeString(E.Message);
        LogBridgeFailure(
          'uFPC_RunCompiler',
          LastError
          + ' | CompilerPath=' + compilerPathText
          + ' | WorkingDirectory=' + workingDirectoryText
          + ' | CommandLine=' + commandLineText
          + ' | ExceptionClass=' + UnicodeString(E.ClassName));
        resultInfo^.ApiStatus := UFPC_API_E_INTERNAL_ERROR;
        Result := UFPC_API_E_INTERNAL_ERROR;
      end;
  end;
  processHandle.Free;
end;

function uFPC_CreateHostExecutable(
  runtimeImagePath: PWideChar;
  executablePath: PWideChar): LongInt; stdcall;
var
  runtimeImagePathText: UnicodeString;
  executablePathText: UnicodeString;
  effectiveExecutablePath: UnicodeString;
  lastErrorCode: DWORD;
begin
  ResetLastState;

  runtimeImagePathText := WideTextOf(runtimeImagePath);
  executablePathText := WideTextOf(executablePath);

  if runtimeImagePathText = '' then
    begin
      LastError := 'uFPC_CreateHostExecutable requires a runtime image path.';
      LogBridgeFailure('uFPC_CreateHostExecutable', LastError);
      Exit(UFPC_API_E_INVALID_ARGUMENT);
    end;

  effectiveExecutablePath := DeriveHostExecutablePath(runtimeImagePathText, executablePathText);
  if effectiveExecutablePath = '' then
    begin
      LastError := 'uFPC_CreateHostExecutable could not derive an executable path.';
      LogBridgeFailure(
        'uFPC_CreateHostExecutable',
        LastError + ' | RuntimeImagePath=' + runtimeImagePathText);
      Exit(UFPC_API_E_INVALID_ARGUMENT);
    end;

  if CompareText(ExpandWidePath(runtimeImagePathText), ExpandWidePath(effectiveExecutablePath)) <> 0 then
    begin
      if not TryCopyFileWide(runtimeImagePathText, effectiveExecutablePath) then
        begin
          lastErrorCode := GetLastError;
          LastError := 'Failed to create host executable copy: ' + UnicodeString(SysErrorMessage(lastErrorCode));
          LogBridgeFailure(
            'uFPC_CreateHostExecutable',
            LastError
            + ' | RuntimeImagePath=' + runtimeImagePathText
            + ' | ExecutablePath=' + effectiveExecutablePath
            + ' | Win32Error=' + UnicodeString(IntToStr(lastErrorCode)));
          Exit(UFPC_API_E_INTERNAL_ERROR);
        end;
    end;

  if not CopyCompanionDebugFile(runtimeImagePathText, effectiveExecutablePath) then
    begin
      lastErrorCode := GetLastError;
      LastError := 'Failed to copy companion debug file: ' + UnicodeString(SysErrorMessage(lastErrorCode));
      LogBridgeFailure(
        'uFPC_CreateHostExecutable',
        LastError
        + ' | RuntimeImagePath=' + runtimeImagePathText
        + ' | ExecutablePath=' + effectiveExecutablePath
        + ' | Win32Error=' + UnicodeString(IntToStr(lastErrorCode)));
      Exit(UFPC_API_E_INTERNAL_ERROR);
    end;

  LastOutput := effectiveExecutablePath;
  Result := UFPC_API_OK;
end;

function uFPC_CopyLastOutput(buffer: PWideChar; bufferChars: Cardinal): Cardinal; stdcall;
begin
  Result := CopyWideString(LastOutput, buffer, bufferChars);
end;

function uFPC_CopyLastError(buffer: PWideChar; bufferChars: Cardinal): Cardinal; stdcall;
begin
  Result := CopyWideString(LastError, buffer, bufferChars);
end;

function uFPC_GetDebugArchitecture(sessionInfo: PuFpcDebugSessionInfo): LongInt; stdcall;
begin
  if not Assigned(sessionInfo) or
     (sessionInfo^.StructSize < SizeOf(TuFpcDebugSessionInfo)) then
    Exit(UFPC_API_E_INVALID_ARGUMENT);

  FillChar(sessionInfo^, SizeOf(TuFpcDebugSessionInfo), 0);
  sessionInfo^.StructSize := SizeOf(TuFpcDebugSessionInfo);
  sessionInfo^.Backend := UFPC_DEBUG_BACKEND_GDB_MI;
  sessionInfo^.DebugFormat := UFPC_DEBUG_FORMAT_DWARF;
  sessionInfo^.Capabilities :=
    UFPC_DEBUG_CAP_BREAKPOINTS or
    UFPC_DEBUG_CAP_WATCHES or
    UFPC_DEBUG_CAP_REGISTERS or
    UFPC_DEBUG_CAP_CALLSTACK or
    UFPC_DEBUG_CAP_SOURCE_PATHS;
  Result := UFPC_API_OK;
end;

function uFPC_OpenDebugSession(
  request: PuFpcDebugSessionRequest;
  sessionHandle: PUInt64): LongInt; stdcall;
begin
  if Assigned(sessionHandle) then
    sessionHandle^ := 0;
  if not Assigned(request) or
     (request^.StructSize < SizeOf(TuFpcDebugSessionRequest)) then
    begin
      LogBridgeFailure('uFPC_OpenDebugSession', 'Invalid debug session request.');
      Exit(UFPC_API_E_INVALID_ARGUMENT);
    end;

  LastError := 'uFPC debug session backend is not implemented yet. '
             + 'The required architecture is fixed to DWARF + GDB/MI.';
  LogBridgeFailure('uFPC_OpenDebugSession', LastError);
  Result := UFPC_API_E_NOT_IMPLEMENTED;
end;

function uFPC_CloseDebugSession(sessionHandle: QWord): LongInt; stdcall;
begin
  if sessionHandle <> 0 then
    begin
      LastError := 'uFPC debug session backend is not implemented yet.';
      LogBridgeFailure('uFPC_CloseDebugSession', LastError);
      Exit(UFPC_API_E_NOT_IMPLEMENTED);
    end;
  Result := UFPC_API_OK;
end;

exports
  uFPC_GetApiVersion name 'uFPC_GetApiVersion',
  uFPC_RunCompiler name 'uFPC_RunCompiler',
  uFPC_CreateHostExecutable name 'uFPC_CreateHostExecutable',
  uFPC_CopyLastOutput name 'uFPC_CopyLastOutput',
  uFPC_CopyLastError name 'uFPC_CopyLastError',
  uFPC_GetDebugArchitecture name 'uFPC_GetDebugArchitecture',
  uFPC_OpenDebugSession name 'uFPC_OpenDebugSession',
  uFPC_CloseDebugSession name 'uFPC_CloseDebugSession';

begin
end.
