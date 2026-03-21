#!/bin/bash
# ============================================================
# Setup Nginx and Let's Encrypt (Certbot) on EC2
# Run this on the EC2 instance
# ============================================================

DOMAIN=$1

EMAIL=$2

if [ -z "$DOMAIN" ] || [ -z "$EMAIL" ]; then
    echo "Usage: ./setup-ssl.sh <your-domain.com> <your-email@example.com>"
    exit 1
fi

echo "=== Installing Nginx and Certbot ==="
sudo dnf update -y
sudo dnf install -y nginx python3-certbot-nginx

echo "=== Configuring Nginx as Proxy ==="
sudo bash -c "cat > /etc/nginx/conf.d/ping-server.conf <<'EOF'
server {
    listen 80;
    server_name $DOMAIN;

    location / {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_cache_bypass \$http_upgrade;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF"

# Replace the placeholder $DOMAIN inside the file since we used single quotes for EOF to prevent shell expansion
sudo sed -i "s/\$DOMAIN/$DOMAIN/g" /etc/nginx/conf.d/ping-server.conf

sudo systemctl enable nginx
sudo systemctl start nginx

echo "=== Requesting SSL Certificate ==="
sudo certbot --nginx -d $DOMAIN --non-interactive --agree-tos -m $EMAIL

echo "=== Done! Your server should be at https://$DOMAIN ==="
