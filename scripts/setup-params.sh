#!/bin/bash
# ============================================================
# Store all Ping server secrets in AWS SSM Parameter Store
# Run this from your local machine (requires aws cli configured)
# Prompts for sensitive values — nothing is hardcoded
# ============================================================

REGION="us-east-1"
PREFIX="/ping-server"

put_secret() {
  if [ -n "$2" ]; then
    echo "Updating $1..."
    aws ssm put-parameter --name "$PREFIX/$1" --type SecureString --region $REGION --overwrite --value "$2" > /dev/null
  else
    echo "Skipping $1 (kept previous value)"
  fi
}

put_string() {
  if [ -n "$2" ]; then
    echo "Updating $1..."
    aws ssm put-parameter --name "$PREFIX/$1" --type String --region $REGION --overwrite --value "$2" > /dev/null
  else
    echo "Skipping $1 (kept previous value)"
  fi
}

# Read a secret from user input
read_secret() {
  local prompt="$1"
  local var
  read -p "$prompt (Leave empty to keep current): " var
  echo "$var"
}

# Read a value from user input (visible)
read_value() {
  local prompt="$1"
  local default="$2"
  local var
  read -p "$prompt [$default] (Leave empty to keep default): " var
  echo "${var:-$default}"
}

echo "=== Ping Server — SSM Parameter Setup ==="
echo ""
echo "NOTE: If you just press Enter, the script will skip updating that variable and keep your existing AWS SSM value."
echo ""

# Generate JWT Key automatically
read -p "Regenerate JWT Key? (This will log everyone out) [y/N]: " REGEN_JWT
if [[ "$REGEN_JWT" =~ ^[Yy]$ ]]; then
  JWT_KEY=$(openssl rand -base64 64 | tr -d '\n')
  echo "Generated new JWT Key: ${JWT_KEY:0:20}... (truncated)"
else
  JWT_KEY=""
fi

# Prompt for secrets
echo ""
echo "--- Database ---"
RDS_HOST=$(read_value "RDS Host" "pingdb.cgxoy8w8eaea.us-east-1.rds.amazonaws.com")
RDS_USER=$(read_value "RDS Username" "rmeji3")
RDS_PASS=$(read_secret "RDS Password")

echo ""
echo "--- API Keys ---"
GOOGLE_KEY=$(read_secret "Google API Key")
GOOGLE_CID=$(read_value "Google Client ID" "")
OPENAI_KEY=$(read_secret "OpenAI API Key")

echo ""
echo "--- AWS (S3/SES) ---"
AWS_AK=$(read_secret "AWS Access Key")
AWS_SK=$(read_secret "AWS Secret Key")
AWS_BUCKET=$(read_value "S3 Bucket" "ping-app")

echo ""
echo "--- Expo ---"
EXPO_TOKEN=$(read_secret "Expo Access Token")

echo ""
echo "=== Storing parameters in SSM ($REGION) ==="

# Connection Strings
if [ -n "$RDS_PASS" ]; then
  put_secret "AUTH_CONNECTION" "Host=$RDS_HOST;Database=ping_auth;Username=$RDS_USER;Password=$RDS_PASS"
  put_secret "APP_CONNECTION"  "Host=$RDS_HOST;Database=ping_app;Username=$RDS_USER;Password=$RDS_PASS"
else
  echo "Skipping AUTH_CONNECTION & APP_CONNECTION (no password provided)"
fi

put_string "REDIS_CONNECTION" 'localhost:6379,abortConnect=false'
put_string "DatabaseProvider" 'Postgres'

# JWT
put_secret "JWT_KEY"                  "$JWT_KEY"
put_string "JWT_ISSUER"               'api.ping-app.net'
put_string "JWT_AUDIENCE"             'api.ping-app.net'
put_string "JWT_ACCESS_TOKEN_MINUTES" '30'
put_string "JWT_REFRESH_TOKEN_DAYS"   '30'

# Google
put_secret "GOOGLE_API_KEY"   "$GOOGLE_KEY"
put_secret "GOOGLE_CLIENT_ID" "$GOOGLE_CID"

# OpenAI
put_secret "OPENAI_API_KEY" "$(echo "$OPENAI_KEY" | tr -d '\r\n')"

# Rate Limiting
put_string "RATE_LIMIT_GLOBAL_PER_MINUTE"          '10000'
put_string "RATE_LIMIT_AUTHENTICATED_PER_MINUTE"    '2000'
put_string "RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE"    '50'
put_string "RATE_LIMIT_PLACE_CREATION_PER_DAY"      '100'

# AWS
put_secret "AWS__AccessKey"   "$AWS_AK"
put_secret "AWS__SecretKey"   "$AWS_SK"
put_string "AWS__Region"      'us-east-1'
put_string "AWS__BucketName"  "$AWS_BUCKET"

# Expo
put_secret "Expo__AccessToken" "$EXPO_TOKEN"

echo ""
echo "=== Done! All AWS SSM updates complete ==="
echo "To apply changes, SSH into EC2 and run: ~/start-server.sh"
