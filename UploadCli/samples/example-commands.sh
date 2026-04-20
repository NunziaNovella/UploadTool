#!/usr/bin/env bash
# Example commands for UploadCli
# Adjust the placeholder values before running.

TENANT="your-tenant-id-or-name.onmicrosoft.com"
ENVIRONMENT="Production"
COMPANY="CRONUS International Ltd."
CLIENT_ID="your-entra-app-client-id"
CLIENT_SECRET="your-entra-app-client-secret"

# ── Upload Customer master data from CSV ──────────────────────────────────────
dotnet run --project UploadCli -- upload \
  --tenant        "$TENANT" \
  --environment   "$ENVIRONMENT" \
  --company       "$COMPANY" \
  --client-id     "$CLIENT_ID" \
  --client-secret "$CLIENT_SECRET" \
  --input         samples/sample-rows.csv \
  --format        csv \
  --table-id      18 \
  --key-field-ids 2 \
  --batch-size    500 \
  --run-triggers  false \
  --error-log     errors-customers.json

# ── Upload from JSON ──────────────────────────────────────────────────────────
dotnet run --project UploadCli -- upload \
  --tenant        "$TENANT" \
  --environment   "$ENVIRONMENT" \
  --company       "$COMPANY" \
  --client-id     "$CLIENT_ID" \
  --client-secret "$CLIENT_SECRET" \
  --input         samples/sample-rows.json \
  --format        json \
  --table-id      18 \
  --key-field-ids 2 \
  --batch-size    250

# ── Publish a release binary and run ─────────────────────────────────────────
dotnet publish UploadCli -c Release -r linux-x64 --self-contained -o dist/

./dist/uploadcli upload \
  --tenant        "$TENANT" \
  --environment   "$ENVIRONMENT" \
  --company       "$COMPANY" \
  --client-id     "$CLIENT_ID" \
  --client-secret "$CLIENT_SECRET" \
  --input         /data/items.csv \
  --format        csv \
  --table-id      27 \
  --key-field-ids 1 \
  --batch-size    1000
