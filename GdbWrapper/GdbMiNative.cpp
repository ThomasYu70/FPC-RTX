// GDB/MI 파서 구현 — 네이티브 C++ (CLR 없음)
#include "GdbMiNative.h"
#include <cctype>
#include <stdexcept>
#include <algorithm>

namespace GdbMi {

// ── 내부 파서 함수 선언 ────────────────────────────────────────────────────

static std::string parseString(const std::string& s, size_t& pos);
static std::shared_ptr<Value> parseValue(const std::string& s, size_t& pos);
static void parseTuple(const std::string& s, size_t& pos, Value& out);
static void parseList (const std::string& s, size_t& pos, Value& out);

// ── 문자열 언이스케이프 ────────────────────────────────────────────────────

// GDB/MI c-string: 따옴표 사이, 백슬래시 이스케이프 포함
static std::string parseString(const std::string& s, size_t& pos) {
    if (pos >= s.size() || s[pos] != '"') return {};
    pos++;  // 여는 '"' 건너뜀

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
    if (pos < s.size()) pos++;  // 닫는 '"' 건너뜀
    return result;
}

// ── 값 파서 ───────────────────────────────────────────────────────────────

static std::shared_ptr<Value> parseValue(const std::string& s, size_t& pos) {
    auto v = std::make_shared<Value>();
    if (pos >= s.size()) return v;

    if (s[pos] == '"') {
        v->type = Value::Type::String;
        v->str  = parseString(s, pos);
    } else if (s[pos] == '{') {
        v->type = Value::Type::Tuple;
        pos++;  // '{'
        parseTuple(s, pos, *v);
        if (pos < s.size() && s[pos] == '}') pos++;
    } else if (s[pos] == '[') {
        v->type = Value::Type::List;
        pos++;  // '['
        parseList(s, pos, *v);
        if (pos < s.size() && s[pos] == ']') pos++;
    }
    return v;
}

// ── 튜플 파서: key=value (, key=value)* ────────────────────────────────────

static void parseTuple(const std::string& s, size_t& pos, Value& out) {
    out.type = Value::Type::Tuple;
    while (pos < s.size() && s[pos] != '}' && s[pos] != ']') {
        // 공백 건너뜀
        while (pos < s.size() && std::isspace((unsigned char)s[pos])) pos++;
        if (pos >= s.size() || s[pos] == '}' || s[pos] == ']') break;

        // key 파싱 (영숫자 / '-' / '_')
        size_t keyStart = pos;
        while (pos < s.size() && s[pos] != '=' && s[pos] != '}'
                               && s[pos] != ']' && s[pos] != ',')
            pos++;
        std::string key = s.substr(keyStart, pos - keyStart);

        if (pos < s.size() && s[pos] == '=') {
            pos++;  // '='
            auto val = parseValue(s, pos);
            out.fields.emplace_back(std::move(key), std::move(val));
        }

        // ',' 건너뜀
        if (pos < s.size() && s[pos] == ',') pos++;
    }
}

// ── 리스트 파서 ────────────────────────────────────────────────────────────

static void parseList(const std::string& s, size_t& pos, Value& out) {
    out.type = Value::Type::List;
    if (pos >= s.size() || s[pos] == ']') return;

    // 헤드를 미리 살펴보고 named(key=value) 리스트인지 판별
    // 첫 비공백 문자가 '{'나 '"'이면 값 리스트,
    // 아니면 key= 형태인지 확인
    size_t peek = pos;
    while (peek < s.size() && std::isspace((unsigned char)s[peek])) peek++;

    bool isNamedList = false;
    if (peek < s.size() && s[peek] != '"' && s[peek] != '{' && s[peek] != '[') {
        // 다음 '='이 ','나 ']'보다 먼저 나오면 named list
        size_t p2 = peek;
        int depth = 0;
        while (p2 < s.size()) {
            char c = s[p2];
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') { if (depth == 0) break; depth--; }
            else if (c == '=' && depth == 0) { isNamedList = true; break; }
            else if (c == ',' && depth == 0) break;
            p2++;
        }
    }

    if (isNamedList) {
        // named list → Tuple로 파싱하여 fields에 저장
        parseTuple(s, pos, out);
    } else {
        // 값 리스트
        while (pos < s.size() && s[pos] != ']') {
            while (pos < s.size() && std::isspace((unsigned char)s[pos])) pos++;
            if (pos >= s.size() || s[pos] == ']') break;
            auto val = parseValue(s, pos);
            out.items.push_back(std::move(val));
            while (pos < s.size() && std::isspace((unsigned char)s[pos])) pos++;
            if (pos < s.size() && s[pos] == ',') pos++;
        }
    }
}

// ── 값 접근자 구현 ─────────────────────────────────────────────────────────

std::string Value::getString(const std::string& key,
                             const std::string& def) const {
    const Value* v = get(key);
    if (v && v->type == Type::String) return v->str;
    return def;
}

const Value* Value::get(const std::string& key) const {
    for (const auto& [k, v] : fields)
        if (k == key && v) return v.get();
    return nullptr;
}

// ── 라인 파서 진입점 ──────────────────────────────────────────────────────

MiLine parseLine(const std::string& line) {
    MiLine result;
    if (line.empty()) return result;

    // 프롬프트
    if (line == "(gdb)" || line == "(gdb) ") {
        result.isPrompt = true;
        return result;
    }

    size_t pos = 0;

    // 선택적 토큰 (선행 숫자)
    int token = -1;
    if (pos < line.size() && std::isdigit((unsigned char)line[pos])) {
        size_t start = pos;
        while (pos < line.size() && std::isdigit((unsigned char)line[pos])) pos++;
        try { token = std::stoi(line.substr(start, pos - start)); }
        catch (...) {}
    }

    if (pos >= line.size()) return result;
    char prefix = line[pos];

    // ── 결과 레코드 (^) ─────────────────────────────────────────────────
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
    // ── 비동기 레코드 (*, +, =) ─────────────────────────────────────────
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
    // ── 스트림 레코드 (~, @, &) ─────────────────────────────────────────
    else if (prefix == '~' || prefix == '@' || prefix == '&') {
        pos++;
        auto rec   = std::make_unique<StreamRecord>();
        rec->channel = prefix;
        if (pos < line.size() && line[pos] == '"')
            rec->text = parseString(line, pos);
        result.stream = std::move(rec);
    }

    return result;
}

} // namespace GdbMi
