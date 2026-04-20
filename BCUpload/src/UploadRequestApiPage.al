/// <summary>
/// OData v4 API page that exposes the upload endpoint.
///
/// Endpoint (after publishing as API page):
///   POST /api/nunziaNovella/upload/v1.0/companies({id})/uploadRequests
///
/// Request body example:
///   { "payloadText": "{\"tableId\":18,\"mode\":\"upsert\",\"runTriggers\":false,
///       \"rows\":[{\"fields\":[{\"id\":2,\"value\":\"C00010\"},{\"id\":3,\"value\":\"Test\"}]}]}" }
///
/// The payloadText is the inner upload payload serialised as a JSON string.
/// The response will contain responseText with the structured result JSON.
/// </summary>
page 50100 "BC Upload API"
{
    PageType = API;
    APIPublisher = 'nunziaNovella';
    APIGroup = 'upload';
    APIVersion = 'v1.0';
    EntityName = 'uploadRequest';
    EntitySetName = 'uploadRequests';
    Caption = 'BC Upload API';
    SourceTable = "BC Upload Request";
    InsertAllowed = true;
    ModifyAllowed = false;
    DeleteAllowed = false;
    ODataKeyFields = RequestId;
    ApplicationArea = All;

    layout
    {
        area(Content)
        {
            field(requestId; Rec.RequestId)
            {
                Caption = 'requestId';
                ApplicationArea = All;
            }
            field(payloadText; PayloadTextVar)
            {
                Caption = 'payloadText';
                ApplicationArea = All;
            }
            field(responseText; ResponseTextVar)
            {
                Caption = 'responseText';
                ApplicationArea = All;
                Editable = false;
            }
            field(statusCode; Rec.StatusCode)
            {
                Caption = 'statusCode';
                ApplicationArea = All;
                Editable = false;
            }
            field(createdAt; Rec.CreatedAt)
            {
                Caption = 'createdAt';
                ApplicationArea = All;
                Editable = false;
            }
            field(errorMessage; Rec.ErrorMessage)
            {
                Caption = 'errorMessage';
                ApplicationArea = All;
                Editable = false;
            }
        }
    }

    var
        PayloadTextVar: Text;
        ResponseTextVar: Text;

    /// <summary>
    /// Populate computed text variables from stored Blob fields when reading a record.
    /// </summary>
    trigger OnAfterGetRecord()
    var
        PayloadInStream: InStream;
        ResponseInStream: InStream;
    begin
        Clear(PayloadTextVar);
        Clear(ResponseTextVar);

        Rec.CalcFields(PayloadBlob, ResponseBlob);

        if Rec.PayloadBlob.HasValue() then begin
            Rec.PayloadBlob.CreateInStream(PayloadInStream, TextEncoding::UTF8);
            PayloadInStream.ReadText(PayloadTextVar);
        end;

        if Rec.ResponseBlob.HasValue() then begin
            Rec.ResponseBlob.CreateInStream(ResponseInStream, TextEncoding::UTF8);
            ResponseInStream.ReadText(ResponseTextVar);
        end;
    end;

    /// <summary>
    /// Called when a new record is about to be inserted.
    /// Reads payloadText, processes the upsert, writes responseText, and completes
    /// the insert.  Returning false tells the platform to skip the default insert;
    /// we call Rec.Insert() ourselves after populating all fields.
    /// </summary>
    trigger OnInsertRecord(BelowxRec: Boolean): Boolean
    var
        UpsertMgmt: Codeunit "BC Upsert Mgmt";
        PayloadOutStream: OutStream;
        ResponseOutStream: OutStream;
        ResponseText: Text;
    begin
        if IsNullGuid(Rec.RequestId) then
            Rec.RequestId := CreateGuid();
        Rec.CreatedAt := CurrentDateTime();

        if PayloadTextVar = '' then begin
            Rec.StatusCode := 400;
            Rec.ErrorMessage := 'payloadText must not be empty.';
            Rec.Insert(false);
            exit(false);
        end;

        // Persist the raw payload
        Rec.PayloadBlob.CreateOutStream(PayloadOutStream, TextEncoding::UTF8);
        PayloadOutStream.WriteText(PayloadTextVar);

        // Run the upsert engine
        ResponseText := UpsertMgmt.ProcessPayload(PayloadTextVar);

        // Persist the response
        Rec.ResponseBlob.CreateOutStream(ResponseOutStream, TextEncoding::UTF8);
        ResponseOutStream.WriteText(ResponseText);

        Rec.StatusCode := 200;
        Rec.Insert(false);

        // Refresh computed variables for the outbound response
        ResponseTextVar := ResponseText;

        exit(false); // we already called Insert
    end;
}
