#!/bin/bash
# ============================================================
# Pull image from ECR and run with secrets from SSM
# Run this on the EC2 instance
# ============================================================

REGION="us-east-1"
PREFIX="/ping-server"
IMAGE="084128132616.dkr.ecr.us-east-1.amazonaws.com/ping-server:latest"

# Helper: fetch parameter from SSM
p() { aws ssm get-parameter --name "$PREFIX/$1" --with-decryption --query "Parameter.Value" --output text --region $REGION; }

echo "=== Authenticating with ECR ==="
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin 084128132616.dkr.ecr.$REGION.amazonaws.com

echo "=== Ensuring Redis is running ==="
if ! [ "$(docker ps -a -q -f name=redis)" ]; then
    echo "Starting new Redis container..."
    docker run -d --name redis --restart always -p 6379:6379 redis:alpine
else
    if ! [ "$(docker ps -q -f name=redis)" ]; then
        echo "Restarting Redis container..."
        docker start redis
    else
        echo "Redis is already running."
    fi
fi

echo "=== Pulling latest image ==="
docker pull $IMAGE

echo "=== Stopping old container (if any) ==="
docker stop ping-server 2>/dev/null
docker rm ping-server 2>/dev/null

echo "=== Fetching secrets from SSM ==="
echo "=== Starting container ==="

docker run -d \
  --name ping-server \
  --network host \
  --restart always \
  -p 8080:8080 \
  -e AUTH_CONNECTION="$(p AUTH_CONNECTION)" \
  -e APP_CONNECTION="$(p APP_CONNECTION)" \
  -e REDIS_CONNECTION="$(p REDIS_CONNECTION)" \
  -e DatabaseProvider="$(p DatabaseProvider)" \
  -e JWT_KEY="$(p JWT_KEY)" \
  -e JWT_ISSUER="$(p JWT_ISSUER)" \
  -e JWT_AUDIENCE="$(p JWT_AUDIENCE)" \
  -e JWT_ACCESS_TOKEN_MINUTES="$(p JWT_ACCESS_TOKEN_MINUTES)" \
  -e JWT_REFRESH_TOKEN_DAYS="$(p JWT_REFRESH_TOKEN_DAYS)" \
  -e GOOGLE_API_KEY="$(p GOOGLE_API_KEY)" \
  -e GOOGLE_CLIENT_ID="$(p GOOGLE_CLIENT_ID)" \
  -e OPENAI_API_KEY="$(p OPENAI_API_KEY)" \
  -e RATE_LIMIT_GLOBAL_PER_MINUTE="$(p RATE_LIMIT_GLOBAL_PER_MINUTE)" \
  -e RATE_LIMIT_AUTHENTICATED_PER_MINUTE="$(p RATE_LIMIT_AUTHENTICATED_PER_MINUTE)" \
  -e RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE="$(p RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE)" \
  -e RATE_LIMIT_PLACE_CREATION_PER_DAY="$(p RATE_LIMIT_PLACE_CREATION_PER_DAY)" \
  -e AWS__AccessKey="$(p AWS__AccessKey)" \
  -e AWS__SecretKey="$(p AWS__SecretKey)" \
  -e AWS__Region="$(p AWS__Region)" \
  -e AWS__BucketName="$(p AWS__BucketName)" \
  $IMAGE

echo ""
echo "=== Container started! ==="
echo "Tailing logs (Ctrl+C to stop)..."
docker logs -f ping-server
