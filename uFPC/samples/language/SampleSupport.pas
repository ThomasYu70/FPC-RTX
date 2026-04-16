unit SampleSupport;

{$mode objfpc}{$H+}

interface

uses
  SysUtils;

const
  MaxAcceptedTotal = 50;
  RetryBitSeed = 1;
  ScaleFactor = 2;

type
  TMode = (umIdle, umRun, umFault);

  TIntArray3 = array[0..2] of LongInt;

  TSensorRecord = record
    Name: string;
    Values: TIntArray3;
    RetryCount: LongInt;
    Offset: Double;
    Mode: TMode;
  end;

  ESampleError = class(Exception);

  TSampleState = class
  private
    FScaledValue: LongInt;
    function GetScaledValue: LongInt;
    procedure SetScaledValue(AValue: LongInt);
  public
    constructor Create;
    function LoadAndScale(const Source: TIntArray3): LongInt;
    function ValidateSensor(const Sensor: TSensorRecord): Boolean;
    property ScaledValue: LongInt read GetScaledValue write SetScaledValue;
  end;

function SumArray(const Source: TIntArray3): LongInt;
procedure FillSensor(var Sensor: TSensorRecord; const Title: string; const Values: TIntArray3);

implementation

function SumArray(const Source: TIntArray3): LongInt;
var
  Index: LongInt;
begin
  Result := 0;
  for Index := 0 to 2 do
    Result := Result + Source[Index];
end;

procedure FillSensor(var Sensor: TSensorRecord; const Title: string; const Values: TIntArray3);
begin
  Sensor.Name := Title;
  Sensor.Values := Values;
  Sensor.RetryCount := 0;
  Sensor.Offset := 0.0;
  Sensor.Mode := umIdle;
end;

constructor TSampleState.Create;
begin
  inherited Create;
  FScaledValue := 0;
end;

function TSampleState.GetScaledValue: LongInt;
begin
  Result := FScaledValue;
end;

procedure TSampleState.SetScaledValue(AValue: LongInt);
begin
  if AValue < 0 then
    FScaledValue := 0
  else
    FScaledValue := AValue;
end;

function TSampleState.LoadAndScale(const Source: TIntArray3): LongInt;
var
  Index: LongInt;
  LocalValue: LongInt;
begin
  Result := 0;
  for Index := 0 to 2 do
  begin
    LocalValue := Source[Index];
    if LocalValue < 0 then
      Continue;
    Result := Result + LocalValue;
    if Result > MaxAcceptedTotal then
      Break;
  end;

  FScaledValue := (Result * ScaleFactor) + (Result div 3) - (Result mod 2);
end;

function TSampleState.ValidateSensor(const Sensor: TSensorRecord): Boolean;
var
  BitMask: LongInt;
begin
  BitMask := (Sensor.RetryCount shl 1) xor RetryBitSeed;
  if (Sensor.Name = '') or (Sensor.Values[0] < 0) then
    Exit(False);

  Result := not ((Sensor.Mode = umFault) and (BitMask > 6));
  Result := Result or (Sensor.Mode = umRun);
end;

end.
