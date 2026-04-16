program DebugTest;

{$mode objfpc}{$H+}

{ 디버거 동작 확인용 샘플
  브레이크포인트 추천 위치:
    - 22행 : Sum 함수 진입
    - 35행 : 루프 시작
    - 38행 : 누적 합산
    - 47행 : 짝수/홀수 분기
}

// ── 함수 ──────────────────────────────────────────────────────────────────────

function Sum(A, B: Integer): Integer;
var
  Result_: Integer;
begin
  Result_ := A + B;       // <-- 브레이크포인트 (22행)
  Result  := Result_;
end;

function IsEven(N: Integer): Boolean;
begin
  Result := (N mod 2) = 0;
end;

// ── 메인 ──────────────────────────────────────────────────────────────────────

var
  i, Total: Integer;
  S: string;

begin
  Total := 0;

  for i := 1 to 5 do        // <-- 브레이크포인트 (35행)
  begin
    Total := Sum(Total, i);  // <-- 브레이크포인트 (37행)

    if IsEven(i) then        // <-- 브레이크포인트 (39행)
      S := 'even'
    else
      S := 'odd';

    WriteLn('i=', i, '  total=', Total, '  ', S);
  end;

  WriteLn('---');
  WriteLn('Final total : ', Total);
  WriteLn('Sum(10,20)  : ', Sum(10, 20));
end.
