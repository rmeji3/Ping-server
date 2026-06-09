#!/bin/bash
set -euo pipefail
# ============================================================
# Pull image from ECR and run with secrets from SSM
# Run this on the EC2 instance
# ============================================================

REGION="us-east-1"
PREFIX="/ping-server"
IMAGE="084128132616.dkr.ecr.us-east-1.amazonaws.com/ping-server:latest"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "$SCRIPT_DIR/docker-compose.server.yml" ]]; then
  ROOT_DIR="$SCRIPT_DIR"
elif [[ -f "$SCRIPT_DIR/../docker-compose.server.yml" ]]; then
  ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
else
  echo "ERROR: docker-compose.server.yml not found next to or above $SCRIPT_DIR" >&2
  exit 1
fi
ENV_FILE="$ROOT_DIR/.env.server"
COMPOSE_FILE="$ROOT_DIR/docker-compose.server.yml"

# Helper: fetch parameter from SSM
p() { aws ssm get-parameter --name "$PREFIX/$1" --with-decryption --query "Parameter.Value" --output text --region $REGION; }

echo "=== Authenticating with ECR ==="
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin 084128132616.dkr.ecr.$REGION.amazonaws.com

echo "=== Pulling latest image ==="
docker pull $IMAGE

echo "=== Fetching secrets from SSM ==="
cat > "$ENV_FILE" <<EOF
PING_IMAGE=$IMAGE
AUTH_CONNECTION=$(p AUTH_CONNECTION)
APP_CONNECTION=$(p APP_CONNECTION)
REDIS_CONNECTION=$(p REDIS_CONNECTION)
DatabaseProvider=$(p DatabaseProvider)
JWT_KEY=$(p JWT_KEY)
JWT_ISSUER=$(p JWT_ISSUER)
JWT_AUDIENCE=$(p JWT_AUDIENCE)
JWT_ACCESS_TOKEN_MINUTES=$(p JWT_ACCESS_TOKEN_MINUTES)
JWT_REFRESH_TOKEN_DAYS=$(p JWT_REFRESH_TOKEN_DAYS)
GOOGLE_API_KEY=$(p GOOGLE_API_KEY)
GOOGLE_CLIENT_ID=$(p GOOGLE_CLIENT_ID)
OPENAI_API_KEY=$(p OPENAI_API_KEY)
RATE_LIMIT_GLOBAL_PER_MINUTE=$(p RATE_LIMIT_GLOBAL_PER_MINUTE)
RATE_LIMIT_AUTHENTICATED_PER_MINUTE=$(p RATE_LIMIT_AUTHENTICATED_PER_MINUTE)
RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE=$(p RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE)
RATE_LIMIT_PLACE_CREATION_PER_DAY=$(p RATE_LIMIT_PLACE_CREATION_PER_DAY)
AWS__AccessKey=$(p AWS__AccessKey)
AWS__SecretKey=$(p AWS__SecretKey)
AWS__Region=$(p AWS__Region)
AWS__BucketName=$(p AWS__BucketName)
Expo__AccessToken=$(p Expo__AccessToken)
EOF

echo "=== Restarting services with Docker Compose ==="
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" pull
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d

echo ""
echo "=== Services started! ==="
echo "Tailing logs (Ctrl+C to stop)..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" logs -f ping-server
