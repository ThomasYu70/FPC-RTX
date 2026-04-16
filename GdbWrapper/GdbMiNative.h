#pragma once
// GDB/MI 파서 — 네이티브 C++ (CLR 없음)
//
// 파싱 대상:
//   결과 레코드 : token "^" result-class ( "," results )?
//   비동기 레코드: token ("*"|"+"|"=") async-type ( "," results )?
//   스트림 레코드: ("~"|"@"|"&") c-string
//   프롬프트    : "(gdb)"

#include <string>
#include <vector>
#include <memory>

namespace GdbMi {

// ── 값 타입 ────────────────────────────────────────────────────────────────

struct Value {
    enum class Type { String, Tuple, List };
    Type type = Type::String;

    std::string str;                                              // String
    std::vector<std::pair<std::string, std::shared_ptr<Value>>> fields;  // Tuple
    std::vector<std::shared_ptr<Value>> items;                   // List (unnamed)

    // 편의 접근자
    std::string getString(const std::string& key,
                          const std::string& def = "") const;
    const Value* get(const std::string& key) const;

    bool empty() const {
        return type == Type::String && str.empty()
            && fields.empty() && items.empty();
    }
};

// ── 레코드 타입 ────────────────────────────────────────────────────────────

enum class ResultClass { Done, Running, Connected, Error, Exit };
enum class AsyncClass  { Exec, Status, Notify };

struct ResultRecord {
    int         token = -1;
    ResultClass cls   = ResultClass::Done;
    Value       results;   // Tuple
};

struct AsyncRecord {
    int        token = -1;
    AsyncClass cls   = AsyncClass::Exec;
    std::string type;       // "stopped", "running", …
    Value       results;
};

struct StreamRecord {
    char        channel;   // '~'=console  '@'=target  '&'=log
    std::string text;
};

struct MiLine {
    std::unique_ptr<ResultRecord> result;
    std::unique_ptr<AsyncRecord>  async;
    std::unique_ptr<StreamRecord> stream;
    bool isPrompt = false;
};

// ── 파서 진입점 ────────────────────────────────────────────────────────────

MiLine parseLine(const std::string& line);

} // namespace GdbMi
