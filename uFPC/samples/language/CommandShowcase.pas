program CommandShowcase;

{$mode objfpc}{$H+}

uses
  SysUtils,
  SampleSupport;

const
  SampleName = 'Mixer';
  RetryLimit = 2;

var
  InputValues: TIntArray3;
  Sensor: TSensorRecord;
  State: TSampleState;
  Sum: LongInt;
  Index: LongInt;
  Flags: LongInt;
  Countdown: LongInt;
  Ready: Boolean;
  MessageText: string;
begin
  InputValues[0] := 4;
  InputValues[1] := 7;
  InputValues[2] := 11;

  Sensor.Name := '';
  Sensor.RetryCount := 0;
  Sensor.Offset := 0.0;
  Sensor.Mode := umIdle;
  FillSensor(Sensor, SampleName, InputValues);
  Sensor.Mode := umRun;
  State := nil;

  try
    State := TSampleState.Create;
    Sum := State.LoadAndScale(InputValues);
    Sensor.Offset := Sum / 2.0;
    Sensor.RetryCount := 0;

    for Index := 0 to 2 do
    begin
      if Sensor.Values[Index] = 0 then
        Continue;
      Sensor.RetryCount := Sensor.RetryCount + 1;
      if Sensor.RetryCount > RetryLimit then
        Break;
    end;

    Countdown := 2;
    while Countdown >= 0 do
    begin
      Flags := (Countdown shr 1) xor Sensor.RetryCount;
      if Flags > 5 then
        Break;
      Countdown := Countdown - 1;
    end;

    repeat
      Sensor.RetryCount := Sensor.RetryCount - 1;
    until Sensor.RetryCount = 0;

    case Sensor.Mode of
      umIdle:
        State.ScaledValue := State.ScaledValue + 1;
      umRun:
        State.ScaledValue := State.ScaledValue + Sum;
      umFault:
        State.ScaledValue := 0;
    end;

    Ready := State.ValidateSensor(Sensor);
    try
      if not Ready then
        raise ESampleError.Create('Sensor validation failed');

      if (State.ScaledValue >= 0) and (Sum > 0) then
        MessageText := 'READY'
      else
        Exit;
    except
      on E: ESampleError do
      begin
        Sensor.Mode := umFault;
        MessageText := E.Message;
      end;
      on E: Exception do
      begin
        Sensor.Mode := umFault;
        MessageText := E.Message;
      end;
    end;

    if MessageText <> '' then
      State.ScaledValue := State.ScaledValue + Length(MessageText)
    else
      State.ScaledValue := State.ScaledValue + SumArray(InputValues);
  finally
    if State <> nil then
      State.Free;
  end;
end.
