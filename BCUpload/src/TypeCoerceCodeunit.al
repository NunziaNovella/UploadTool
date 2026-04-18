/// <summary>
/// Provides type-safe coercion utilities for FieldRef assignment and filtering.
///
/// BC FieldRef.Value accepts a Variant, but assigning the wrong underlying type
/// can cause runtime errors. This codeunit converts JsonToken/JsonValue to the
/// concrete AL type that matches the FieldRef's declared type before assignment.
///
/// Supported FieldTypes:
///   Text, Code, Integer, BigInteger, Decimal, Boolean,
///   Date, DateTime, Time, Guid, Option/Enum, and a catch-all Text fallback.
/// </summary>
codeunit 50101 "BC Type Coerce"
{
    Access = Public;

    // -------------------------------------------------------------------------
    // SetFieldValue: write a JSON token value into a FieldRef
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assigns the value carried by ValueToken to FRef using the correct AL type.
    /// </summary>
    procedure SetFieldValue(var FRef: FieldRef; ValueToken: JsonToken)
    begin
        if not ValueToken.IsValue() then
            Error('Expected a scalar JSON value for field %1, but got an object or array.', FRef.Number());

        case FRef.Type() of
            FieldType::Text, FieldType::Code:
                FRef.Value := ValueToken.AsValue().AsText();

            FieldType::Integer:
                FRef.Value := ValueToken.AsValue().AsInteger();

            FieldType::BigInteger:
                FRef.Value := SetBigIntValue(ValueToken.AsValue().AsText());

            FieldType::Decimal:
                FRef.Value := ValueToken.AsValue().AsDecimal();

            FieldType::Boolean:
                FRef.Value := ValueToken.AsValue().AsBoolean();

            FieldType::Date:
                FRef.Value := SetDateValue(ValueToken.AsValue().AsText());

            FieldType::Time:
                FRef.Value := SetTimeValue(ValueToken.AsValue().AsText());

            FieldType::DateTime:
                FRef.Value := SetDateTimeValue(ValueToken.AsValue().AsText());

            FieldType::Guid:
                FRef.Value := SetGuidValue(ValueToken.AsValue().AsText());

            FieldType::Option:
                // Accepts either the ordinal integer or the option caption text.
                SetOptionValue(FRef, ValueToken);

            else
                // Catch-all: attempt text assignment and let BC coerce further.
                FRef.Value := ValueToken.AsValue().AsText();
        end;
    end;

    // -------------------------------------------------------------------------
    // SetFieldFilter: build an equality filter on a FieldRef for record lookup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets an equality filter on FRef using the JSON value, converting to the
    /// correct native type so that FindFirst operates on a typed filter.
    /// </summary>
    procedure SetFieldFilter(var FRef: FieldRef; ValueToken: JsonToken)
    var
        TextValue: Text;
    begin
        if not ValueToken.IsValue() then
            Error('Cannot use a non-scalar JSON value as a filter for field %1.', FRef.Number());

        TextValue := ValueToken.AsValue().AsText();

        case FRef.Type() of
            FieldType::Text, FieldType::Code:
                FRef.SetRange(TextValue);

            FieldType::Integer, FieldType::Option:
                FRef.SetRange(ValueToken.AsValue().AsInteger());

            FieldType::BigInteger:
                FRef.SetRange(SetBigIntValue(TextValue));

            FieldType::Decimal:
                FRef.SetRange(ValueToken.AsValue().AsDecimal());

            FieldType::Boolean:
                FRef.SetRange(ValueToken.AsValue().AsBoolean());

            FieldType::Date:
                FRef.SetRange(SetDateValue(TextValue));

            FieldType::Time:
                FRef.SetRange(SetTimeValue(TextValue));

            FieldType::DateTime:
                FRef.SetRange(SetDateTimeValue(TextValue));

            FieldType::Guid:
                FRef.SetRange(SetGuidValue(TextValue));

            else
                FRef.SetFilter(TextValue);
        end;
    end;

    // -------------------------------------------------------------------------
    // Private conversion helpers
    // -------------------------------------------------------------------------

    local procedure SetBigIntValue(TextValue: Text): BigInteger
    var
        BigIntValue: BigInteger;
    begin
        Evaluate(BigIntValue, TextValue);
        exit(BigIntValue);
    end;

    local procedure SetDateValue(TextValue: Text): Date
    var
        DateValue: Date;
    begin
        // Accepts ISO 8601 date strings (YYYY-MM-DD) and BC-formatted dates.
        Evaluate(DateValue, TextValue);
        exit(DateValue);
    end;

    local procedure SetTimeValue(TextValue: Text): Time
    var
        TimeValue: Time;
    begin
        Evaluate(TimeValue, TextValue);
        exit(TimeValue);
    end;

    local procedure SetDateTimeValue(TextValue: Text): DateTime
    var
        DateTimeValue: DateTime;
    begin
        Evaluate(DateTimeValue, TextValue);
        exit(DateTimeValue);
    end;

    local procedure SetGuidValue(TextValue: Text): Guid
    var
        GuidValue: Guid;
    begin
        Evaluate(GuidValue, TextValue);
        exit(GuidValue);
    end;

    /// <summary>
    /// For Option/Enum fields, accepts either:
    ///   - a JSON integer  -> used directly as the ordinal
    ///   - a JSON string   -> matched case-insensitively against option member captions
    /// Falls back to integer 0 if no match is found.
    /// </summary>
    local procedure SetOptionValue(var FRef: FieldRef; ValueToken: JsonToken)
    var
        OrdinalValue: Integer;
        CaptionValue: Text;
        OptionCaptions: Text;
        CaptionList: List of [Text];
        Caption: Text;
        Idx: Integer;
    begin
        if ValueToken.AsValue().IsInteger() then begin
            FRef.Value := ValueToken.AsValue().AsInteger();
            exit;
        end;

        CaptionValue := ValueToken.AsValue().AsText().ToLower();
        OptionCaptions := FRef.OptionCaption();
        CaptionList := OptionCaptions.Split(',');

        Idx := 0;
        foreach Caption in CaptionList do begin
            if Caption.Trim().ToLower() = CaptionValue then begin
                FRef.Value := Idx;
                exit;
            end;
            Idx += 1;
        end;

        // No match — leave at default (0)
        FRef.Value := 0;
    end;
}
