/// <summary>
/// Buffer table that stores each inbound upload request and its processed response.
/// The API page (50100) writes to PayloadBlob on POST and reads from ResponseBlob
/// after the upsert codeunit has run.
/// </summary>
table 50100 "BC Upload Request"
{
    Caption = 'BC Upload Request';
    DataClassification = SystemMetadata;
    DataPerCompany = true;

    fields
    {
        field(1; RequestId; Guid)
        {
            Caption = 'Request Id';
            DataClassification = SystemMetadata;
        }
        field(2; PayloadBlob; Blob)
        {
            Caption = 'Payload (Blob)';
            DataClassification = SystemMetadata;
        }
        field(3; ResponseBlob; Blob)
        {
            Caption = 'Response (Blob)';
            DataClassification = SystemMetadata;
        }
        field(4; StatusCode; Integer)
        {
            Caption = 'Status Code';
            DataClassification = SystemMetadata;
        }
        field(5; CreatedAt; DateTime)
        {
            Caption = 'Created At';
            DataClassification = SystemMetadata;
        }
        field(6; ErrorMessage; Text[2048])
        {
            Caption = 'Error Message';
            DataClassification = SystemMetadata;
        }
    }

    keys
    {
        key(PK; RequestId)
        {
            Clustered = true;
        }
    }
}
