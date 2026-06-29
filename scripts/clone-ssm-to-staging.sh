#!/usr/bin/env bash
set -euo pipefail
# ============================================================
# Clone /ping-server/* SSM params -> /ping-staging/* with the
# few overrides that make staging isolated from prod.
#
# Overrides applied:
#   AUTH_CONNECTION / APP_CONNECTION  -> swap prod DB host for staging host
#   JWT_KEY                           -> fresh random value
#   AWS__BucketName                   -> staging bucket
# Everything else is copied verbatim (same type, SecureString stays encrypted).
#
# Safe by default: prints a plan WITHOUT secret values. Nothing is written
# unless you pass --apply. Run from your laptop with AWS creds that can read
# /ping-server/* and write /ping-staging/*.
#
# Requires: aws cli v2, jq, openssl
# ============================================================

REGION="us-east-1"
SRC_PREFIX="/ping-server"
DST_PREFIX="/ping-staging"

# --- EDIT THESE THREE ---------------------------------------
PROD_DB_HOST="pingdb.cgxoy8w8eaea.us-east-1.rds.amazonaws.com"
STAGING_DB_HOST="ping-staging.cgxoy8w8eaea.us-east-1.rds.amazonaws.com"   # ping-staging RDS endpoint
STAGING_BUCKET="ping-app-staging"                       # separate S3 bucket for staging
# ------------------------------------------------------------

APPLY=false
[[ "${1:-}" == "--apply" ]] && APPLY=true

# Sanity checks
command -v jq >/dev/null      || { echo "ERROR: jq not installed" >&2; exit 1; }
command -v openssl >/dev/null || { echo "ERROR: openssl not installed" >&2; exit 1; }
if [[ "$STAGING_DB_HOST" == "REPLACE_WITH_PING_STAGING_ENDPOINT" ]]; then
  echo "ERROR: set STAGING_DB_HOST to your ping-staging RDS endpoint first." >&2
  exit 1
fi

# One fresh JWT key reused for both connection-independent uses (just JWT_KEY here).
NEW_JWT_KEY="$(openssl rand -base64 48)"

echo "=== Reading params from ${SRC_PREFIX}/* (region ${REGION}) ==="
PARAMS_JSON="$(aws ssm get-parameters-by-path \
  --path "$SRC_PREFIX" \
  --recursive \
  --with-decryption \
  --region "$REGION" \
  --output json)"

COUNT="$(echo "$PARAMS_JSON" | jq '.Parameters | length')"
if [[ "$COUNT" -eq 0 ]]; then
  echo "ERROR: no params found under ${SRC_PREFIX}. Check the prefix/creds." >&2
  exit 1
fi
echo "Found ${COUNT} params."
echo
$APPLY && echo "=== APPLYING (writing to ${DST_PREFIX}/*) ===" \
        || echo "=== DRY RUN (no writes). Re-run with --apply to write. ==="
echo

# Iterate params
echo "$PARAMS_JSON" | jq -c '.Parameters[]' | while read -r row; do
  name="$(echo "$row"  | jq -r '.Name')"
  type="$(echo "$row"  | jq -r '.Type')"
  value="$(echo "$row" | jq -r '.Value')"

  suffix="${name#$SRC_PREFIX/}"          # e.g. APP_CONNECTION
  dst_name="${DST_PREFIX}/${suffix}"
  note=""

  case "$suffix" in
    AUTH_CONNECTION|APP_CONNECTION)
      value="${value//$PROD_DB_HOST/$STAGING_DB_HOST}"
      note="(host -> staging)"
      ;;
    JWT_KEY)
      value="$NEW_JWT_KEY"
      note="(fresh random)"
      ;;
    AWS__BucketName)
      value="$STAGING_BUCKET"
      note="(staging bucket)"
      ;;
    *)
      note="(copied)"
      ;;
  esac

  echo "  ${dst_name}  [${type}] ${note}"

  if $APPLY; then
    aws ssm put-parameter \
      --name "$dst_name" \
      --value "$value" \
      --type "$type" \
      --overwrite \
      --region "$REGION" >/dev/null
  fi
done

echo
if $APPLY; then
  echo "=== Done. ${DST_PREFIX}/* written. ==="
  echo "Sanity check (names only): aws ssm get-parameters-by-path --path ${DST_PREFIX} --recursive --query 'Parameters[].Name' --output text --region ${REGION}"
else
  echo "Dry run complete. Verify the plan above, set STAGING_DB_HOST, then re-run with --apply."
fi
