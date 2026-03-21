#!/bin/bash
# ============================================================
# Store all Ping server secrets in AWS SSM Parameter Store
# Run this from your local machine (requires aws cli configured)
# Prompts for sensitive values — nothing is hardcoded
# ============================================================

REGION="us-east-1"
PREFIX="/ping-server"

put_secret() { aws ssm put-parameter --name "$PREFIX/$1" --type SecureString --region $REGION --overwrite --value "$2"; }
put_string() { aws ssm put-parameter --name "$PREFIX/$1" --type String --region $REGION --overwrite --value "$2"; }

# Read a secret from user input (hidden)
read_secret() {
  local prompt="$1"
  local var
  read -sp "$prompt: " var
  echo ""
  echo "$var"
}

# Read a value from user input (visible)
read_value() {
  local prompt="$1"
  local default="$2"
  local var
  read -p "$prompt [$default]: " var
  echo "${var:-$default}"
}

echo "=== Ping Server — SSM Parameter Setup ==="
echo ""

# Generate JWT Key automatically
JWT_KEY=$(openssl rand -base64 64 | tr -d '\n')
echo "Generated JWT Key: ${JWT_KEY:0:20}... (truncated)"

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
echo "--- AWS (S3/SNS/SES) ---"
AWS_AK=$(read_secret "AWS Access Key")
AWS_SK=$(read_secret "AWS Secret Key")
AWS_BUCKET=$(read_value "S3 Bucket" "ping-app")

echo ""
echo "=== Storing parameters in SSM ($REGION) ==="

# Connection Strings
put_secret "AUTH_CONNECTION" "Host=$RDS_HOST;Database=ping_auth;Username=$RDS_USER;Password=$RDS_PASS"
put_secret "APP_CONNECTION"  "Host=$RDS_HOST;Database=ping_app;Username=$RDS_USER;Password=$RDS_PASS"
put_string "REDIS_CONNECTION" 'localhost:6379,abortConnect=false'
put_string "DatabaseProvider" 'Postgres'

# JWT
put_secret "JWT_KEY"                  "$JWT_KEY"
put_string "JWT_ISSUER"               'PingServer'
put_string "JWT_AUDIENCE"             'PingApp'
put_string "JWT_ACCESS_TOKEN_MINUTES" '30'
put_string "JWT_REFRESH_TOKEN_DAYS"    '30'

# Google
put_secret "GOOGLE_API_KEY"   "$GOOGLE_KEY"
put_secret "GOOGLE_CLIENT_ID" "$GOOGLE_CID"

# OpenAI
put_secret "OPENAI_API_KEY" "$OPENAI_KEY"

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

echo ""
echo "=== Done! All secrets stored in SSM ==="
echo "Now SSH into EC2 and run: ~/start-server.sh"
