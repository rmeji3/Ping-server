#!/bin/bash
set -e  # Exit immediately on any error
# ============================================================
# Build and Push multi-platform image to ECR
# Run this from your local development machine
# ============================================================

REGION="us-east-1"
REPO="084128132616.dkr.ecr.us-east-1.amazonaws.com/ping-server"

echo "=== Authenticating with ECR ==="
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin $REPO

# Create and use a new builder that supports multi-platform if it doesn't exist
if ! docker buildx inspect multiplatform > /dev/null 2>&1; then
    echo "=== Creating new buildx builder ==="
    docker buildx create --name multiplatform --use
fi

echo "=== Building and Pushing multi-platform image (amd64, arm64) ==="
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t $REPO:latest \
  --push .

echo "=== Done! ==="
