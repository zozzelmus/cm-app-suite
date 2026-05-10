#!/usr/bin/env bash
# One-shot dev helper. Creates the 19 test users in the running Keycloak (matches
# the realm JSON for cold starts) and enables directAccessGrantsEnabled on conduct-bff.
# Re-runnable: existing usernames return 409 and are skipped.
#
# Drift caveat: this script duplicates the user list from
# infra/keycloak/realm/conduct-realm.json + libs/Infrastructure/Seed/SeedConstants.cs.
# See backlog: "consolidate test-user list to a single source of truth".

set -euo pipefail

KC=${KC:-http://localhost:8088}
REALM=${REALM:-conduct}
ADMIN_USER=${ADMIN_USER:-admin}
ADMIN_PASS=${ADMIN_PASS:-admin}

TOKEN=$(curl -s -X POST "$KC/realms/master/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "username=$ADMIN_USER&password=$ADMIN_PASS&grant_type=password&client_id=admin-cli" \
  | grep -oE '"access_token":"[^"]+' | sed 's/"access_token":"//')

[ -n "$TOKEN" ] || { echo "ERR: failed to get admin token"; exit 1; }

# Enable Direct Access Grants on conduct-bff (dev login-as needs ROPC).
CLIENT_UUID=$(curl -s -H "Authorization: Bearer $TOKEN" \
  "$KC/admin/realms/$REALM/clients?clientId=conduct-bff" \
  | grep -oE '"id":"[a-f0-9-]+' | head -1 | sed 's/"id":"//')
curl -s -X PUT "$KC/admin/realms/$REALM/clients/$CLIENT_UUID" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"directAccessGrantsEnabled":true}' > /dev/null
echo "✓ enabled directAccessGrantsEnabled on conduct-bff"

create_user() {
  local username=$1 firstname=$2 lastname=$3
  local payload
  payload=$(cat <<EOF
{
  "username":"$username","enabled":true,"email":"$username@conduct.local","emailVerified":true,
  "firstName":"$firstname","lastName":"$lastname",
  "credentials":[{"type":"password","value":"$username","temporary":false}]
}
EOF
)
  local code
  code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$KC/admin/realms/$REALM/users" \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "$payload")
  case "$code" in
    201) echo "  + created $username" ;;
    409) echo "  · exists  $username" ;;
    *)   echo "  ! failed  $username (HTTP $code)" ;;
  esac
}

create_user "mgr-sui"      "Mia"    "Manager-SUI"
create_user "inv-sui"      "Ian"    "Investigator-SUI"
create_user "mgr-cmp"      "Mara"   "Manager-CMP"
create_user "inv-cmp"      "Igor"   "Investigator-CMP"
create_user "mgr-inv"      "Mei"    "Manager-INV"
create_user "inv-inv"      "Ivo"    "Investigator-INV"
create_user "mgr-inv-apac" "Mina"   "Manager-APAC"
create_user "inv-inv-apac" "Ines"   "Investigator-APAC"
create_user "mgr-inv-in"   "Maya"   "Manager-IN"
create_user "inv-inv-in"   "Ishan"  "Investigator-IN"
create_user "mgr-inv-ph"   "Marcos" "Manager-PH"
create_user "inv-inv-ph"   "Iris"   "Investigator-PH"
create_user "mgr-hr-er"    "Marta"  "Manager-HR-ER"
create_user "inv-hr-er"    "Ilia"   "Investigator-HR-ER"
create_user "mgr-leg"      "Milo"   "Manager-LEG"
create_user "inv-leg"      "Iona"   "Investigator-LEG"
create_user "mgr-ia"       "Mila"   "Manager-IA"
create_user "inv-ia"       "Ilan"   "Investigator-IA"
create_user "sysadmin"     "Sam"    "System-Admin"

echo "done."
