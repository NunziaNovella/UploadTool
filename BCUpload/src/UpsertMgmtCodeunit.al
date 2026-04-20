/// <summary>
/// Processes an upload payload JSON string and performs dynamic upsert
/// operations against arbitrary BC tables using RecordRef / FieldRef.
///
/// Expected payload schema:
/// {
///   "tableId"    : <int>           -- required; the table number to write to
///   "runTriggers": <bool>          -- optional, default false
///   "mode"       : "upsert"        -- optional, only "upsert" is supported
///   "keyFieldIds": [<int>, ...]    -- optional; if absent, primary-key fields are used
///   "rows": [
///     {
///       "fields": [
///         { "id": <int>, "value": <any|null> }
///       ]
///     }
///   ]
/// }
///
/// Response schema:
/// {
///   "accepted"  : <int>,
///   "succeeded" : <int>,
///   "failed"    : <int>,
///   "errors"    : [
///     { "rowIndex": <int>, "fieldId": <int|-1>, "message": <string> }
///   ]
/// }
/// </summary>
codeunit 50100 "BC Upsert Mgmt"
{
    Access = Public;

    /// <summary>
    /// Entry point.  Parses the payload, iterates rows, and returns a JSON result string.
    /// </summary>
    procedure ProcessPayload(PayloadText: Text): Text
    var
        PayloadJson: JsonObject;
        RowsToken: JsonToken;
        KeyFieldIdsToken: JsonToken;
        RowsJson: JsonArray;
        KeyFieldIdsJson: JsonArray;
        ErrorsJson: JsonArray;
        ResponseJson: JsonObject;
        TableId: Integer;
        RunTriggers: Boolean;
        Mode: Text;
        Accepted: Integer;
        Succeeded: Integer;
        Failed: Integer;
        RowToken: JsonToken;
        i: Integer;
        ResponseText: Text;
        Token: JsonToken;
    begin
        if not PayloadJson.ReadFrom(PayloadText) then
            Error('Invalid JSON payload: could not parse root object.');

        // tableId (required)
        if not PayloadJson.Get('tableId', Token) then
            Error('Payload missing required field "tableId".');
        TableId := Token.AsValue().AsInteger();

        // runTriggers (optional, default false)
        RunTriggers := false;
        if PayloadJson.Get('runTriggers', Token) then
            RunTriggers := Token.AsValue().AsBoolean();

        // mode (optional, default "upsert")
        Mode := 'upsert';
        if PayloadJson.Get('mode', Token) then
            Mode := Token.AsValue().AsText();
        if Mode <> 'upsert' then
            Error('Unsupported mode "%1". Only "upsert" is supported.', Mode);

        // rows (required)
        if not PayloadJson.Get('rows', RowsToken) then
            Error('Payload missing required field "rows".');
        RowsJson := RowsToken.AsArray();

        // keyFieldIds (optional)
        Clear(KeyFieldIdsJson);
        if PayloadJson.Get('keyFieldIds', KeyFieldIdsToken) then
            KeyFieldIdsJson := KeyFieldIdsToken.AsArray();

        Accepted := RowsJson.Count();
        Succeeded := 0;
        Failed := 0;

        for i := 0 to RowsJson.Count() - 1 do begin
            RowsJson.Get(i, RowToken);
            if TryUpsertRow(TableId, RowToken.AsObject(), KeyFieldIdsJson, RunTriggers, i, ErrorsJson) then
                Succeeded += 1
            else begin
                Failed += 1;
                AddRowError(ErrorsJson, i, -1, GetLastErrorText());
                ClearLastError();
            end;
        end;

        ResponseJson.Add('accepted', Accepted);
        ResponseJson.Add('succeeded', Succeeded);
        ResponseJson.Add('failed', Failed);
        ResponseJson.Add('errors', ErrorsJson);
        ResponseJson.WriteTo(ResponseText);
        exit(ResponseText);
    end;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to upsert a single row.  Returns false and captures the error
    /// message if anything goes wrong (table not found, field not found, type
    /// coercion failure, etc.).
    /// </summary>
    [TryFunction]
    local procedure TryUpsertRow(TableId: Integer; RowJson: JsonObject; KeyFieldIds: JsonArray; RunTriggers: Boolean; RowIndex: Integer; var ErrorsJson: JsonArray)
    var
        RecRef: RecordRef;
        FieldsToken: JsonToken;
        FieldsJson: JsonArray;
        IsFound: Boolean;
        TypeCoerce: Codeunit "BC Type Coerce";
    begin
        if not RowJson.Get('fields', FieldsToken) then
            Error('Row %1 is missing the "fields" array.', RowIndex);
        FieldsJson := FieldsToken.AsArray();

        RecRef.Open(TableId);

        // Build key-field filters and try to locate the existing record
        IsFound := FindRecord(RecRef, FieldsJson, KeyFieldIds);

        if IsFound then begin
            // Modify: apply all non-null field values
            ApplyFieldValues(RecRef, FieldsJson, TypeCoerce);
            RecRef.Modify(RunTriggers);
        end else begin
            // Insert: re-open fresh, init, then apply all field values
            RecRef.Close();
            RecRef.Open(TableId);
            RecRef.Init();
            ApplyFieldValues(RecRef, FieldsJson, TypeCoerce);
            RecRef.Insert(RunTriggers);
        end;

        RecRef.Close();
    end;

    /// <summary>
    /// Builds equality filters on RecRef for the designated key fields (either
    /// the supplied keyFieldIds or the table's clustered primary key), then
    /// calls FindFirst.  Returns true if a matching record exists.
    /// </summary>
    local procedure FindRecord(var RecRef: RecordRef; FieldsJson: JsonArray; KeyFieldIds: JsonArray): Boolean
    var
        FRef: FieldRef;
        KeyRef: KeyRef;
        ValueToken: JsonToken;
        IdToken: JsonToken;
        FieldId: Integer;
        k: Integer;
        TypeCoerce: Codeunit "BC Type Coerce";
    begin
        RecRef.Reset();

        if KeyFieldIds.Count() > 0 then begin
            // Use caller-supplied key field list
            for k := 0 to KeyFieldIds.Count() - 1 do begin
                KeyFieldIds.Get(k, IdToken);
                FieldId := IdToken.AsValue().AsInteger();
                FRef := RecRef.Field(FieldId);
                if GetFieldValueFromRow(FieldsJson, FieldId, ValueToken) then
                    TypeCoerce.SetFieldFilter(FRef, ValueToken);
            end;
        end else begin
            // Fall back to the table's primary (clustered) key
            KeyRef := RecRef.KeyIndex(1);
            for k := 1 to KeyRef.FieldCount() do begin
                FRef := KeyRef.FieldIndex(k);
                FieldId := FRef.Number();
                if GetFieldValueFromRow(FieldsJson, FieldId, ValueToken) then
                    TypeCoerce.SetFieldFilter(FRef, ValueToken);
            end;
        end;

        exit(RecRef.FindFirst());
    end;

    /// <summary>
    /// Iterates the fields array from the row JSON and sets each non-null value
    /// on the RecordRef via type-coerced assignment.
    /// </summary>
    local procedure ApplyFieldValues(var RecRef: RecordRef; FieldsJson: JsonArray; var TypeCoerce: Codeunit "BC Type Coerce")
    var
        FieldToken: JsonToken;
        FieldObj: JsonObject;
        IdToken: JsonToken;
        ValueToken: JsonToken;
        FRef: FieldRef;
        FieldId: Integer;
        i: Integer;
    begin
        for i := 0 to FieldsJson.Count() - 1 do begin
            FieldsJson.Get(i, FieldToken);
            FieldObj := FieldToken.AsObject();

            if FieldObj.Get('id', IdToken) and FieldObj.Get('value', ValueToken) then begin
                FieldId := IdToken.AsValue().AsInteger();
                // null means "skip this field"
                if not ValueToken.AsValue().IsNull() then begin
                    FRef := RecRef.Field(FieldId);
                    TypeCoerce.SetFieldValue(FRef, ValueToken);
                end;
            end;
        end;
    end;

    /// <summary>
    /// Searches the row's fields array for an entry whose "id" matches FieldId.
    /// Returns true and sets ValueToken if found.
    /// </summary>
    local procedure GetFieldValueFromRow(FieldsJson: JsonArray; FieldId: Integer; var ValueToken: JsonToken): Boolean
    var
        Token: JsonToken;
        FieldObj: JsonObject;
        IdToken: JsonToken;
        i: Integer;
    begin
        for i := 0 to FieldsJson.Count() - 1 do begin
            FieldsJson.Get(i, Token);
            FieldObj := Token.AsObject();
            if FieldObj.Get('id', IdToken) then
                if IdToken.AsValue().AsInteger() = FieldId then begin
                    if FieldObj.Get('value', ValueToken) then
                        exit(true);
                end;
        end;
        exit(false);
    end;

    /// <summary>
    /// Appends a structured error entry to the errors JSON array.
    /// </summary>
    local procedure AddRowError(var ErrorsJson: JsonArray; RowIndex: Integer; FieldId: Integer; Message: Text)
    var
        ErrObj: JsonObject;
    begin
        ErrObj.Add('rowIndex', RowIndex);
        ErrObj.Add('fieldId', FieldId);
        ErrObj.Add('message', Message);
        ErrorsJson.Add(ErrObj);
    end;
}
