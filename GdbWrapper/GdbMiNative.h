#pragma once
// GDB/MI parser -- native C++ (no CLR)
//
// Targets:
//   result record  : token "^" result-class ( "," results )?
//   async record   : token ("*"|"+"|"=") async-type ( "," results )?
//   stream record  : ("~"|"@"|"&") c-string
//   prompt         : "(gdb)"

#include <string>
#include <vector>
#include <memory>

namespace GdbMi {

// ── Value type ─────────────────────────────────────────────────────────────

struct Value {
    enum class Type { String, Tuple, List };
    Type type = Type::String;

    std::string str;   // for Type::String

    // Use typedef to avoid MSVC >>> ambiguity
    using FieldPtr  = std::shared_ptr<Value>;
    using Field     = std::pair<std::string, FieldPtr>;
    using FieldList = std::vector<Field>;
    using ItemList  = std::vector<FieldPtr>;

    FieldList fields;  // for Type::Tuple  (key = value pairs)
    ItemList  items;   // for Type::List   (unnamed values)

    // Convenience accessors
    std::string getString(const std::string& key,
                          const std::string& def = "") const;
    const Value* get(const std::string& key) const;

    bool empty() const {
        return type == Type::String && str.empty()
            && fields.empty() && items.empty();
    }
};

// ── Record types ───────────────────────────────────────────────────────────

enum class ResultClass { Done, Running, Connected, Error, Exit };
enum class AsyncClass  { Exec, Status, Notify };

struct ResultRecord {
    int         token = -1;
    ResultClass cls   = ResultClass::Done;
    Value       results;   // Tuple
};

struct AsyncRecord {
    int         token = -1;
    AsyncClass  cls   = AsyncClass::Exec;
    std::string type;      // "stopped", "running", ...
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

// ── Parser entry point ─────────────────────────────────────────────────────

MiLine parseLine(const std::string& line);

} // namespace GdbMi
