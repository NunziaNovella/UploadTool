# UploadTool – Business Central ETL Upload Tool

A two-tier ETL tool for bulk-uploading data into **Business Central SaaS** using
dynamic RecordRef/FieldRef upserts.

```
CSV / JSON file
      │
      ▼
┌─────────────────────────┐   OAuth2 (Entra ID)   ┌──────────────────────────────────┐
│  UploadCli (.NET 8 CLI) │ ──────────────────────► │  BCUpload (AL Extension / BC API) │
│  • streaming batches    │   OData v4 POST JSON   │  • RecordRef / FieldRef upsert    │
│  • Polly retry          │ ◄────────────────────── │  • structured error response      │
│  • per-row error log    │                         └──────────────────────────────────┘
└─────────────────────────┘
```

## Table of Contents

1. [Architecture](#architecture)
2. [AL Backend Extension (BCUpload)](#al-backend-extension-bcupload)
   - [Payload Schema](#payload-schema)
   - [Response Schema](#response-schema)
   - [Entra App Registration](#entra-app-registration)
   - [Publishing the AL Extension](#publishing-the-al-extension)
3. [.NET CLI Orchestrator (UploadCli)](#net-cli-orchestrator-uploadcli)
   - [Prerequisites](#prerequisites)
   - [Build](#build)
   - [Usage](#usage)
   - [Input Formats](#input-formats)
4. [CI/CD](#cicd)
5. [End-to-End Example](#end-to-end-example)

---

## Architecture

| Layer | Technology | Purpose |
|-------|-----------|---------|
| AL Extension | Business Central AL | Exposes an OData v4 API page; performs dynamic upsert via `RecordRef`/`FieldRef` |
| .NET CLI | .NET 8, System.CommandLine | Streams CSV/JSON → batches → POSTs to BC; handles auth & retries |
| Auth | Entra ID (OAuth2 client credentials) | Non-interactive app identity, no user sign-in required |

### Data Flow

1. **CLI** reads the input file row-by-row (never fully buffered in memory).
2. Rows are accumulated into batches of *N* (default **500**).
3. For each batch the CLI serialises an **`UploadPayload`** JSON string and wraps
   it in an OData entity `{ "payloadText": "..." }`.
4. The CLI POSTs to the BC **`uploadRequests`** API endpoint with a Bearer token.
5. The AL `OnInsertRecord` trigger calls the **`BC Upsert Mgmt`** codeunit.
6. The codeunit iterates rows, uses `RecordRef.FindFirst` to locate existing
   records and then calls `Modify` or `Insert` with optional trigger execution.
7. The BC API returns the structured **`UploadResult`** JSON in `responseText`.
8. The CLI parses the result, emits progress, and writes any row-level errors to
   a NDJSON log file.

---

## AL Backend Extension (BCUpload)

Location: `BCUpload/`

### Object inventory

| ID | Type | Name | Purpose |
|----|------|------|---------|
| 50100 | Table | BC Upload Request | Buffer table – stores inbound payload and outbound response |
| 50100 | Page | BC Upload API | OData v4 API page (`/uploadRequests`) |
| 50100 | Codeunit | BC Upsert Mgmt | Parses JSON payload; drives per-row upsert with error isolation |
| 50101 | Codeunit | BC Type Coerce | Converts JSON values to native AL types for `FieldRef` assignment |

### Payload Schema

The `payloadText` property contains a JSON-serialised `UploadPayload` object:

```json
{
  "tableId":     18,
  "runTriggers": false,
  "mode":        "upsert",
  "keyFieldIds": [2],
  "rows": [
    {
      "fields": [
        { "id": 2, "value": "C00010" },
        { "id": 3, "value": "Acme Corp" },
        { "id": 50, "value": null }
      ]
    }
  ]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `tableId` | integer | ✅ | BC table number |
| `runTriggers` | boolean | — | Run table triggers on insert/modify (default `false`) |
| `mode` | string | — | Only `"upsert"` is supported |
| `keyFieldIds` | integer[] | — | Field IDs to use as upsert key; omit to use the table's primary key |
| `rows[].fields[].id` | integer | ✅ | BC field number |
| `rows[].fields[].value` | any\|null | ✅ | `null` → skip field (do not change) |

#### Supported FieldRef types

`Text`, `Code`, `Integer`, `BigInteger`, `Decimal`, `Boolean`, `Date`, `Time`,
`DateTime`, `Guid`, `Option`/`Enum`.  All others fall back to a text assignment.

Date/Time values must be parseable by AL's `Evaluate` procedure
(e.g. `"2024-01-31"`, `"12:00:00"`, `"2024-01-31T12:00:00.000Z"`).

### Response Schema

```json
{
  "accepted":  3,
  "succeeded": 2,
  "failed":    1,
  "errors": [
    {
      "rowIndex": 2,
      "fieldId":  -1,
      "message":  "The record in table 18 already exists. ..."
    }
  ]
}
```

### Entra App Registration

> Skip this if you already have an app registration with BC API permissions.

1. In the [Azure Portal](https://portal.azure.com) → **Entra ID → App registrations → New registration**.
2. Name: e.g. `BCUploadTool`.  Supported account types: *Single tenant*.
3. After registration, note the **Application (client) ID** and **Tenant ID**.
4. **Certificates & secrets → New client secret** – copy the secret value immediately.
5. **API permissions → Add a permission → Dynamics 365 Business Central → Application permissions**:
   - `API.ReadWrite.All` (or a more restrictive permission like `Automation.ReadWrite.All`)
6. **Grant admin consent** for the tenant.
7. In Business Central, create a **Microsoft Entra app** record:
   - Search for *Microsoft Entra Applications*.
   - New → paste the Client ID → set **State** to *Enabled*.
   - Assign the required permission sets (e.g. `D365 FULL ACCESS` for testing).

### Publishing the AL Extension

**Development / sandbox:**

1. Open `BCUpload/` in VS Code with the AL Language extension.
2. Configure `.vscode/launch.json` with your sandbox connection.
3. Press <kbd>F5</kbd> or run **AL: Publish**.

**Production / SaaS (AppSource bypass):**

```bash
# Build the .app file
al build

# Upload via Admin Center API or BCPT
# https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/administration/tenant-admin-center-environments
```

After publishing, the API is accessible at:

```
POST https://api.businesscentral.dynamics.com/v2.0/{tenantId}/{environment}/api/nunziaNovella/upload/v1.0/companies({companyId})/uploadRequests
```

---

## .NET CLI Orchestrator (UploadCli)

Location: `UploadCli/`

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)

### Build

```bash
# Run from source
dotnet run --project UploadCli -- upload --help

# Publish self-contained binary
dotnet publish UploadCli -c Release -r linux-x64 --self-contained -o dist/
```

### Usage

```
uploadcli upload [options]

Required:
  --tenant          <guid|domain>   Azure AD tenant
  --environment     <name>          BC environment name
  --company         <name>          BC company name (OData URL form)
  --client-id       <guid>          Entra app client ID
  --client-secret   <secret>        Entra app client secret
  --input           <path>          Input CSV or JSON file
  --table-id        <int>           Target BC table number

Optional:
  --base-url        <url>           Override BC SaaS base URL
  --scope           <scope>         OAuth2 scope (default: BC .default)
  --format          csv|json        Input format (default: csv)
  --key-field-ids   <int> ...       Key field IDs for upsert (default: PK)
  --run-triggers                    Run BC table triggers
  --batch-size      <int>           Rows per HTTP batch (default: 500)
  --error-log       <path>          NDJSON error log path
```

### Input Formats

#### CSV

Column headers must follow the `id_<fieldId>` convention.
An empty cell is treated as `null` (field skipped).

```csv
id_2,id_3,id_21,id_50
C00010,Acme Corp,TRUE,10000
C00011,Globex Inc,TRUE,5000
```

#### JSON

Top-level array; each element is one row object with a `fields` array.
`null` values are forwarded to AL as "skip".

```json
[
  { "fields": [{ "id": 2, "value": "C00010" }, { "id": 3, "value": "Acme Corp" }] },
  { "fields": [{ "id": 2, "value": "C00011" }, { "id": 50, "value": null }] }
]
```

---

## CI/CD

The workflow at `.github/workflows/ci-dotnet.yml` runs on every push/PR touching
`UploadCli/`:

- Restores NuGet packages
- Builds in Release configuration
- Publishes a self-contained `linux-x64` binary
- Smoke-tests `--help` / `upload --help`

AL extensions cannot be compiled in standard CI without a Business Central
container.  For AL CI, consider
[`microsoft/AL-Go`](https://github.com/microsoft/AL-Go) or the
[BC Container Helper](https://github.com/microsoft/navcontainerhelper).

---

## End-to-End Example

```bash
# 1. Publish the AL extension to your BC sandbox/SaaS environment (VS Code / AL: Publish).

# 2. Upload Customer master from CSV
dotnet run --project UploadCli -- upload \
  --tenant        "contoso.onmicrosoft.com" \
  --environment   "Production" \
  --company       "CRONUS International Ltd." \
  --client-id     "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
  --client-secret "your-secret" \
  --input         UploadCli/samples/sample-rows.csv \
  --format        csv \
  --table-id      18 \
  --key-field-ids 2 \
  --batch-size    500

# Expected output:
#   Target : https://api.businesscentral.dynamics.com/v2.0/contoso.onmicrosoft.com/Production
#   Company: CRONUS International Ltd.  Table: 18
#   Input  : sample-rows.csv  (CSV)
#   Batch  : 500 rows
#
#     Batch 1 (4 rows) … accepted=4  ok=4  failed=0
#
#   Done. Accepted=4  Succeeded=4  Failed=0
```
