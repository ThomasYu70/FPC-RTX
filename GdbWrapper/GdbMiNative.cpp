// GDB/MI parser implementation -- native C++ (no CLR)
#include "GdbMiNative.h"
#include <cctype>

namespace GdbMi {

// ── Forward declarations ───────────────────────────────────────────────────

static std::string     parseString(const std::string& s, size_t& pos);
static Value::FieldPtr parseValue (const std::string& s, size_t& pos);
static void            parseTuple (const std::string& s, size_t& pos, Value& out);
static void            parseList  (const std::string& s, size_t& pos, Value& out);

// ── C-string parser (GDB/MI quoted string with backslash escapes) ──────────

static std::string parseString(const std::string& s, size_t& pos) {
    if (pos >= s.size() || s[pos] != '"') return {};
    pos++;  // skip opening '"'

    std::string result;
    result.reserve(64);
    while (pos < s.size() && s[pos] != '"') {
        if (s[pos] == '\\' && pos + 1 < s.size()) {
            char esc = s[pos + 1];
            switch (esc) {
                case 'n':  result += '\n'; pos += 2; break;
                case 't':  result += '\t'; pos += 2; break;
                case 'r':  result += '\r'; pos += 2; break;
                case '"':  result += '"';  pos += 2; break;
                case '\\': result += '\\'; pos += 2; break;
                default:   result += s[pos]; pos++;  break;
            }
        } else {
            result += s[pos++];
        }
    }
    if (pos < s.size()) pos++;  // skip closing '"'
    return result;
}

// ── Value parser ──────────────────────────────────────────────────────────

static Value::FieldPtr parseValue(const std::string& s, size_t& pos) {
    auto v = std::make_shared<Value>();
    if (pos >= s.size()) return v;

    if (s[pos] == '"') {
        v->type = Value::Type::String;
        v->str  = parseString(s, pos);
    } else if (s[pos] == '{') {
        v->type = Value::Type::Tuple;
        pos++;  // skip '{'
        parseTuple(s, pos, *v);
        if (pos < s.size() && s[pos] == '}') pos++;
    } else if (s[pos] == '[') {
        v->type = Value::Type::List;
        pos++;  // skip '['
        parseList(s, pos, *v);
        if (pos < s.size() && s[pos] == ']') pos++;
    }
    return v;
}

// ── Tuple parser: key=value (, key=value)* ────────────────────────────────

static void parseTuple(const std::string& s, size_t& pos, Value& out) {
    out.type = Value::Type::Tuple;
    while (pos < s.size() && s[pos] != '}' && s[pos] != ']') {
        // skip whitespace
        while (pos < s.size() && std::isspace((unsigned char)s[pos])) pos++;
        if (pos >= s.size() || s[pos] == '}' || s[pos] == ']') break;

        // parse key  (alphanumeric / '-' / '_')
        size_t keyStart = pos;
        while (pos < s.size() && s[pos] != '='
                               && s[pos] != '}' && s[pos] != ']'
                               && s[pos] != ',')
            pos++;
        std::string key = s.substr(keyStart, pos - keyStart);

        if (pos < s.size() && s[pos] == '=') {
            pos++;  // skip '='
            Value::FieldPtr val = parseValue(s, pos);
            out.fields.push_back(Value::Field(key, val));
        }
        if (pos < s.size() && s[pos] == ',') pos++;
    }
}

// ── List parser ───────────────────────────────────────────────────────────

static void parseList(const std::string& s, size_t& pos, Value& out) {
    out.type = Value::Type::List;
    if (pos >= s.size() || s[pos] == ']') return;

    // Look ahead: if first non-space token contains '=' before ',' or ']'
    // it is a named list (list of results), otherwise a value list.
    size_t peek = pos;
    while (peek < s.size() && std::isspace((unsigned char)s[peek])) peek++;

    bool isNamed = false;
    if (peek < s.size() && s[peek] != '"' && s[peek] != '{' && s[peek] != '[') {
        size_t p2 = peek;
        int depth = 0;
        while (p2 < s.size()) {
            char c = s[p2];
            if (c == '{' || c == '[') depth++;
            else if ((c == '}' || c == ']') && depth == 0) break;
            else if (c == '=' && depth == 0) { isNamed = true; break; }
            else if (c == ',' && depth == 0) break;
            p2++;
        }
    }

    if (isNamed) {
        parseTuple(s, pos, out);   // store as fields
    } else {
        while (pos < s.size() && s[pos] != ']') {
            while (pos < s.size() && std::isspace((unsigned char)s[pos])) pos++;
            if (pos >= s.size() || s[pos] == ']') break;
            out.items.push_back(parseValue(s, pos));
            while (pos < s.size() && std::isspace((unsigned char)s[pos])) pos++;
            if (pos < s.size() && s[pos] == ',') pos++;
        }
    }
}

// ── Value accessors ───────────────────────────────────────────────────────

std::string Value::getString(const std::string& key,
                             const std::string& def) const {
    const Value* v = get(key);
    if (v && v->type == Type::String) return v->str;
    return def;
}

const Value* Value::get(const std::string& key) const {
    for (size_t i = 0; i < fields.size(); i++) {
        if (fields[i].first == key && fields[i].second)
            return fields[i].second.get();
    }
    return nullptr;
}

// ── Main line parser ──────────────────────────────────────────────────────

MiLine parseLine(const std::string& line) {
    MiLine result;
    if (line.empty()) return result;

    // GDB prompt
    if (line == "(gdb)" || line == "(gdb) ") {
        result.isPrompt = true;
        return result;
    }

    size_t pos = 0;

    // Optional leading token (digits)
    int token = -1;
    if (pos < line.size() && std::isdigit((unsigned char)line[pos])) {
        size_t start = pos;
        while (pos < line.size() && std::isdigit((unsigned char)line[pos])) pos++;
        try { token = std::stoi(line.substr(start, pos - start)); }
        catch (...) {}
    }

    if (pos >= line.size()) return result;
    char prefix = line[pos];

    // ── Result record (^) ────────────────────────────────────────────────
    if (prefix == '^') {
        pos++;
        auto rec = std::make_unique<ResultRecord>();
        rec->token = token;

        size_t clsStart = pos;
        while (pos < line.size() && line[pos] != ',' && line[pos] != '\r') pos++;
        std::string cls = line.substr(clsStart, pos - clsStart);

        if      (cls == "done")      rec->cls = ResultClass::Done;
        else if (cls == "running")   rec->cls = ResultClass::Running;
        else if (cls == "connected") rec->cls = ResultClass::Connected;
        else if (cls == "error")     rec->cls = ResultClass::Error;
        else if (cls == "exit")      rec->cls = ResultClass::Exit;

        if (pos < line.size() && line[pos] == ',') {
            pos++;
            parseTuple(line, pos, rec->results);
        }
        result.result = std::move(rec);
    }
    // ── Async record (*, +, =) ───────────────────────────────────────────
    else if (prefix == '*' || prefix == '+' || prefix == '=') {
        pos++;
        auto rec = std::make_unique<AsyncRecord>();
        rec->token = token;
        rec->cls   = (prefix == '*') ? AsyncClass::Exec   :
                     (prefix == '+') ? AsyncClass::Status :
                                       AsyncClass::Notify;

        size_t typeStart = pos;
        while (pos < line.size() && line[pos] != ',' && line[pos] != '\r') pos++;
        rec->type = line.substr(typeStart, pos - typeStart);

        if (pos < line.size() && line[pos] == ',') {
            pos++;
            parseTuple(line, pos, rec->results);
        }
        result.async = std::move(rec);
    }
    // ── Stream record (~, @, &) ──────────────────────────────────────────
    else if (prefix == '~' || prefix == '@' || prefix == '&') {
        pos++;
        auto rec     = std::make_unique<StreamRecord>();
        rec->channel = prefix;
        if (pos < line.size() && line[pos] == '"')
            rec->text = parseString(line, pos);
        result.stream = std::move(rec);
    }

    return result;
}

} // namespace GdbMi
