#!/usr/bin/env bash
# Configures the Calendar integration secrets + app settings for the deployed
# Functions worker. Secrets live in Key Vault; the Function app settings reference
# them. Re-runnable: the AES token-encryption key is generated once and never
# overwritten (overwriting it would orphan already-encrypted tokens).
#
# Usage:
#   export OUTLOOK_CLIENT_ID="..."
#   export OUTLOOK_CLIENT_SECRET="..."     # treat as sensitive; do not paste in shared logs
#   export OUTLOOK_TENANT_ID="common"      # or your tenant GUID
#   # optional, for Google (leave unset to skip):
#   export GOOGLE_CLIENT_ID="..."
#   export GOOGLE_CLIENT_SECRET="..."
#   ./scripts/configure-calendar-azure.sh
#
# Requires: az CLI logged in (az login) with rights on the resource group + Key Vault.
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-ar-Aluki}"
FUNCTION_APP_NAME="${FUNCTION_APP_NAME:-func-araluki-dev-6155}"
KEYVAULT_NAME="${KEYVAULT_NAME:-kvaralukidev6155}"
# Confirm the real default hostname with: az functionapp show -g "$RESOURCE_GROUP" -n "$FUNCTION_APP_NAME" --query defaultHostName -o tsv
CALLBACK_BASE_URL="${CALLBACK_BASE_URL:-https://${FUNCTION_APP_NAME}.azurewebsites.net}"

kv_ref() { echo "@Microsoft.KeyVault(SecretUri=https://${KEYVAULT_NAME}.vault.azure.net/secrets/$1/)"; }

set_secret() { # name value
  az keyvault secret set --vault-name "$KEYVAULT_NAME" --name "$1" --value "$2" --output none
  echo "  ✓ Key Vault secret: $1"
}

ensure_random_secret() { # name  (generates a base64 32-byte key only if absent)
  if az keyvault secret show --vault-name "$KEYVAULT_NAME" --name "$1" --query id -o tsv >/dev/null 2>&1; then
    echo "  • Key Vault secret already exists, keeping: $1"
  else
    set_secret "$1" "$(openssl rand -base64 32)"
  fi
}

settings=()

echo "==> Shared calendar settings"
ensure_random_secret "Calendar-TokenEncryptionKey"
ensure_random_secret "Calendar-LinkSigningKey"
settings+=(
  "Calendar__CallbackBaseUrl=${CALLBACK_BASE_URL}"
  "Calendar__TokenEncryptionKey=$(kv_ref Calendar-TokenEncryptionKey)"
  "Calendar__LinkSigningKey=$(kv_ref Calendar-LinkSigningKey)"
  "Calendar__ConnectLinkExpiryMinutes=30"
)

if [[ -n "${OUTLOOK_CLIENT_ID:-}" && -n "${OUTLOOK_CLIENT_SECRET:-}" ]]; then
  echo "==> Outlook (Microsoft Graph)"
  set_secret "Calendar-Outlook-ClientSecret" "$OUTLOOK_CLIENT_SECRET"
  settings+=(
    "Calendar__Outlook__Enabled=true"
    "Calendar__Outlook__ClientId=${OUTLOOK_CLIENT_ID}"
    "Calendar__Outlook__ClientSecret=$(kv_ref Calendar-Outlook-ClientSecret)"
    "Calendar__Outlook__TenantId=${OUTLOOK_TENANT_ID:-common}"
    "Calendar__Outlook__Scopes__0=Calendars.ReadWrite"
    "Calendar__Outlook__Scopes__1=offline_access"
  )
fi

if [[ -n "${GOOGLE_CLIENT_ID:-}" && -n "${GOOGLE_CLIENT_SECRET:-}" ]]; then
  echo "==> Google Calendar"
  set_secret "Calendar-Google-ClientSecret" "$GOOGLE_CLIENT_SECRET"
  settings+=(
    "Calendar__Google__Enabled=true"
    "Calendar__Google__ClientId=${GOOGLE_CLIENT_ID}"
    "Calendar__Google__ClientSecret=$(kv_ref Calendar-Google-ClientSecret)"
    "Calendar__Google__Scopes__0=https://www.googleapis.com/auth/calendar.events"
  )
fi

echo "==> Applying ${#settings[@]} app settings to ${FUNCTION_APP_NAME}"
az functionapp config appsettings set \
  --resource-group "$RESOURCE_GROUP" \
  --name "$FUNCTION_APP_NAME" \
  --settings "${settings[@]}" \
  --output none
echo "  ✓ App settings applied"

echo
echo "Done. Redirect URI to register on each OAuth app:"
echo "  ${CALLBACK_BASE_URL}/api/calendar/callback"
echo
echo "Verify the Function's managed identity can read Key Vault secrets, e.g.:"
echo "  az keyvault secret show --vault-name ${KEYVAULT_NAME} --name Calendar-Outlook-ClientSecret --query id -o tsv"
