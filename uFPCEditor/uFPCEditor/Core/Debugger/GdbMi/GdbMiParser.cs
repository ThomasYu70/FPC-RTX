using System.Text;

namespace uFPCEditor.Core.Debugger.GdbMi;

// ─────────────────────────────────────────────────────────────────────────────
// GDB/MI 프로토콜 파서
// 원본 참조: Org/packages/ide/gdbmiwrap.pas  (TGDBWrapper.ParseResponse)
//
// GDB/MI 출력 문법 (EBNF 요약):
//   output      ::= ( out-of-band-record )* [ result-record ] "(gdb)" nl
//   result-rec  ::= [ token ] "^" result-class ( "," result )* nl
//   async-rec   ::= [ token ] ("*"|"+"|"=") async-class ( "," result )* nl
//   stream-rec  ::= ("~"|"@"|"&") c-string nl
//   result      ::= variable "=" value
//   value       ::= const | tuple | list
//   const       ::= c-string
//   tuple       ::= "{}" | "{" result ( "," result )* "}"
//   list        ::= "[]" | "[" value  ( "," value  )* "]"
//                        | "[" result ( "," result )* "]"
// ─────────────────────────────────────────────────────────────────────────────

public static class GdbMiParser
{
    public static GdbMiLine Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new GdbMiLine();

        line = line.TrimEnd('\r', '\n');

        if (line == "(gdb)")
            return new GdbMiLine { IsPrompt = true };

        // 스트림 레코드: ~, @, &
        if (line[0] is '~' or '@' or '&')
        {
            return new GdbMiLine
            {
                StreamOutput = new GdbStreamOutput
                {
                    Channel = line[0],
                    Text    = ParseCString(line[1..])
                }
            };
        }

        // 토큰 읽기 (숫자)
        int pos = 0;
        string? token = null;
        if (char.IsDigit(line[pos]))
        {
            int start = pos;
            while (pos < line.Length && char.IsDigit(line[pos])) pos++;
            token = line[start..pos];
        }

        if (pos >= line.Length)
            return new GdbMiLine();

        char prefix = line[pos++];

        // 결과 레코드: ^
        if (prefix == '^')
        {
            var rc = ParseResultRecord(line, pos, token);
            return new GdbMiLine { ResultRecord = rc };
        }

        // 비동기 레코드: *, +, =
        if (prefix is '*' or '+' or '=')
        {
            var ac = ParseAsyncRecord(line, pos, token, prefix);
            return new GdbMiLine { AsyncRecord = ac };
        }

        return new GdbMiLine();
    }

    // ── 결과 레코드 ───────────────────────────────────────────────────────────

    private static GdbResultRecord ParseResultRecord(string line, int pos, string? token)
    {
        // result-class: done | running | connected | error | exit
        int comma = line.IndexOf(',', pos);
        string className = comma < 0 ? line[pos..] : line[pos..comma];

        var resultClass = className switch
        {
            "done"      => GdbResultClass.Done,
            "running"   => GdbResultClass.Running,
            "connected" => GdbResultClass.Connected,
            "error"     => GdbResultClass.Error,
            "exit"      => GdbResultClass.Exit,
            _           => GdbResultClass.Done
        };

        var results = new GdbTupleValue();
        if (comma >= 0)
            ParseResultList(line, comma + 1, results);

        return new GdbResultRecord
        {
            Token       = token,
            ResultClass = resultClass,
            Results     = results
        };
    }

    // ── 비동기 레코드 ─────────────────────────────────────────────────────────

    private static GdbAsyncRecord ParseAsyncRecord(string line, int pos, string? token, char prefix)
    {
        var asyncClass = prefix switch
        {
            '*' => GdbAsyncClass.Exec,
            '+' => GdbAsyncClass.Status,
            _   => GdbAsyncClass.Notify
        };

        int comma = line.IndexOf(',', pos);
        string asyncType = comma < 0 ? line[pos..] : line[pos..comma];

        var results = new GdbTupleValue();
        if (comma >= 0)
            ParseResultList(line, comma + 1, results);

        return new GdbAsyncRecord
        {
            Token      = token,
            AsyncClass = asyncClass,
            AsyncType  = asyncType,
            Results    = results
        };
    }

    // ── result 리스트 파싱 ────────────────────────────────────────────────────

    private static void ParseResultList(string line, int pos, GdbTupleValue tuple)
    {
        // key=value, key=value, ...
        while (pos < line.Length)
        {
            // key
            int eq = line.IndexOf('=', pos);
            if (eq < 0) break;
            string key = line[pos..eq].Trim();
            pos = eq + 1;

            // value
            var (val, newPos) = ParseValue(line, pos);
            if (val != null)
                tuple.Fields[key] = val;
            pos = newPos;

            // 콤마 건너뜀
            if (pos < line.Length && line[pos] == ',')
                pos++;
        }
    }

    // ── value 파싱 ────────────────────────────────────────────────────────────

    private static (GdbValue? value, int newPos) ParseValue(string line, int pos)
    {
        if (pos >= line.Length)
            return (null, pos);

        char ch = line[pos];

        if (ch == '"')
        {
            var (str, newPos) = ParseCStringWithPos(line, pos);
            return (new GdbStringValue(str), newPos);
        }

        if (ch == '{')
            return ParseTuple(line, pos);

        if (ch == '[')
            return ParseList(line, pos);

        // 토큰 없이 오는 raw 식별자 (드문 경우)
        int end = pos;
        while (end < line.Length && line[end] != ',' && line[end] != '}' && line[end] != ']')
            end++;
        return (new GdbStringValue(line[pos..end]), end);
    }

    private static (GdbTupleValue, int) ParseTuple(string line, int pos)
    {
        var tuple = new GdbTupleValue();
        pos++; // skip '{'
        while (pos < line.Length && line[pos] != '}')
        {
            if (line[pos] == ',') { pos++; continue; }

            int eq = line.IndexOf('=', pos);
            if (eq < 0) break;
            string key = line[pos..eq].Trim();
            pos = eq + 1;

            var (val, newPos) = ParseValue(line, pos);
            if (val != null) tuple.Fields[key] = val;
            pos = newPos;
        }
        if (pos < line.Length && line[pos] == '}') pos++;
        return (tuple, pos);
    }

    private static (GdbListValue, int) ParseList(string line, int pos)
    {
        var list = new GdbListValue();
        pos++; // skip '['
        while (pos < line.Length && line[pos] != ']')
        {
            if (line[pos] == ',') { pos++; continue; }

            // 현재 위치가 값 시작 문자({, [, ")면 키 없이 바로 파싱
            // 예: register-values=[{number="0",value="0x0"},...]
            bool startsWithValue = line[pos] is '{' or '[' or '"';

            if (!startsWithValue)
            {
                // key=value 형식: 가장 가까운 닫힘 기호 이전에 '='이 있으면 키 스킵
                // 예: stack=[frame={...},frame={...}]
                int nextEq    = line.IndexOf('=', pos);
                int nextComma = line.IndexOf(',', pos);
                int nextBrack = line.IndexOf(']', pos);
                int nextClose = Math.Min(
                    nextComma < 0 ? int.MaxValue : nextComma,
                    nextBrack < 0 ? int.MaxValue : nextBrack);

                if (nextEq > 0 && nextEq < nextClose)
                {
                    pos = nextEq + 1; // skip key=
                }
            }

            var (val, newPos) = ParseValue(line, pos);
            if (val != null) list.Items.Add(val);
            pos = newPos;
        }
        if (pos < line.Length && line[pos] == ']') pos++;
        return (list, pos);
    }

    // ── C 문자열 ──────────────────────────────────────────────────────────────

    private static string ParseCString(string raw)
    {
        if (raw.Length < 2 || raw[0] != '"') return raw;
        var (s, _) = ParseCStringWithPos(raw, 0);
        return s;
    }

    private static (string, int) ParseCStringWithPos(string line, int pos)
    {
        if (pos >= line.Length || line[pos] != '"')
            return (string.Empty, pos);

        pos++; // skip opening "
        var sb = new StringBuilder();

        while (pos < line.Length && line[pos] != '"')
        {
            if (line[pos] == '\\' && pos + 1 < line.Length)
            {
                pos++;
                sb.Append(line[pos] switch
                {
                    'n'  => '\n',
                    'r'  => '\r',
                    't'  => '\t',
                    '"'  => '"',
                    '\\' => '\\',
                    _    => line[pos]
                });
            }
            else
            {
                sb.Append(line[pos]);
            }
            pos++;
        }
        if (pos < line.Length && line[pos] == '"') pos++; // skip closing "
        return (sb.ToString(), pos);
    }

    // ── 편의 메서드: *stopped 레코드에서 DebugStopInfo 추출 ───────────────────

    public static DebugStopInfo ExtractStopInfo(GdbAsyncRecord record)
    {
        var r = record.Results;
        string reasonStr = r.GetString("reason") ?? string.Empty;

        var reason = reasonStr switch
        {
            "breakpoint-hit"       => StopReason.BreakpointHit,
            "end-stepping-range"   => StopReason.EndSteppingRange,
            "function-finished"    => StopReason.FunctionFinished,
            "signal-received"      => StopReason.SignalReceived,
            "exited-normally"      => StopReason.ExitedNormally,
            "exited-signalled"     => StopReason.ExitedWithError,
            "exited"               => StopReason.Exited,
            _                      => StopReason.Unknown
        };

        int.TryParse(r.GetString("bkptno"),   out int bpId);
        int.TryParse(r.GetString("exit-code"), out int exitCode);

        var frame = r.GetTuple("frame");
        string file = frame?.GetString("fullname") ?? frame?.GetString("file") ?? string.Empty;
        int.TryParse(frame?.GetString("line"), out int lineNum);
        string func = frame?.GetString("func") ?? string.Empty;

        return new DebugStopInfo
        {
            Reason       = reason,
            BreakpointId = bpId,
            FileName     = file,
            Line         = lineNum,
            Function     = func,
            SignalName   = r.GetString("signal-name"),
            ExitCode     = exitCode
        };
    }
}
