#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
echo "Running smoke test against $BASE_URL..."

# 1. Obtain admin dev token
echo "Obtaining admin development token..."
TOKEN_RESP=$(curl -s -f -X POST "$BASE_URL/dev/token" \
    -H "Content-Type: application/json" \
    -d '{
        "userId": "smoke_admin",
        "displayName": "Smoke Admin",
        "scopes": ["shop.admin", "shop.customer"]
    }')

ADMIN_TOKEN=$(echo "$TOKEN_RESP" | grep -o '"token":"[^"]*' | cut -d'"' -f4)

if [ -z "$ADMIN_TOKEN" ]; then
    echo "FAIL: Could not obtain admin token from /dev/token"
    exit 1
fi

# 2. Obtain customer dev token
CUSTOMER_ID="smoke_customer_$(date +%s)"
TOKEN_RESP=$(curl -s -f -X POST "$BASE_URL/dev/token" \
    -H "Content-Type: application/json" \
    -d "{
        \"userId\": \"$CUSTOMER_ID\",
        \"displayName\": \"Smoke Customer\",
        \"scopes\": [\"shop.customer\"]
    }")

CUSTOMER_TOKEN=$(echo "$TOKEN_RESP" | grep -o '"token":"[^"]*' | cut -d'"' -f4)

# 3. Create product in catalog
SKU="SMOKE-$(date +%s)"
echo "Creating product with SKU: $SKU..."
PRODUCT_RESP=$(curl -s -f -X POST "$BASE_URL/api/v1/catalog/products" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "Content-Type: application/json" \
    -d "{
        \"sku\": \"$SKU\",
        \"name\": \"Smoke Test Product\",
        \"description\": \"A product for automated smoke testing\",
        \"price\": 49.99
    }")

PRODUCT_ID=$(echo "$PRODUCT_RESP" | grep -o '"id":"[^"]*' | cut -d'"' -f4)
echo "Product created with ID: $PRODUCT_ID"

# Allow outbox processing for ProductCreated
sleep 1

# 4. Set stock in inventory
echo "Setting inventory stock for product..."
curl -s -f -X PUT "$BASE_URL/api/v1/inventory/stock/$PRODUCT_ID" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "Content-Type: application/json" \
    -d '{ "onHand": 50 }'

# 5. Place order with Idempotency-Key
IDEMPOTENCY_KEY="smoke-key-$(date +%s)"
echo "Placing order with Idempotency-Key: $IDEMPOTENCY_KEY..."
ORDER_RESP=$(curl -s -i -X POST "$BASE_URL/api/v1/ordering/orders" \
    -H "Authorization: Bearer $CUSTOMER_TOKEN" \
    -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
    -H "Content-Type: application/json" \
    -d "{
        \"lines\": [
            { \"productId\": \"$PRODUCT_ID\", \"quantity\": 2 }
        ]
    }")

ORDER_ID=$(echo "$ORDER_RESP" | grep -o '"id":"[^"]*' | cut -d'"' -f4 || true)
if [ -z "$ORDER_ID" ]; then
    LOCATION=$(echo "$ORDER_RESP" | grep -i "^Location:" | awk '{print $2}' | tr -d '\r')
    ORDER_ID=$(basename "$LOCATION")
fi

echo "Order placed with ID: $ORDER_ID"

# 6. Poll until Confirmed
echo "Polling order status until Confirmed..."
MAX_ATTEMPTS=20
ATTEMPT=0
CONFIRMED=0

while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
    ATTEMPT=$((ATTEMPT + 1))
    STATUS_RESP=$(curl -s -f -X GET "$BASE_URL/api/v1/ordering/orders/$ORDER_ID" \
        -H "Authorization: Bearer $CUSTOMER_TOKEN")

    STATUS=$(echo "$STATUS_RESP" | grep -o '"status":"[^"]*' | cut -d'"' -f4)
    echo "Attempt $ATTEMPT: Order status is '$STATUS'"

    if [ "$STATUS" = "Confirmed" ]; then
        CONFIRMED=1
        break
    fi

    if [ "$STATUS" = "Cancelled" ]; then
        echo "FAIL: Order was cancelled prematurely"
        exit 1
    fi

    sleep 1
done

if [ $CONFIRMED -ne 1 ]; then
    echo "FAIL: Order did not reach Confirmed status within timeout"
    exit 1
fi

# 7. Check customer notification
echo "Checking customer notifications..."
NOTIF_RESP=$(curl -s -f -X GET "$BASE_URL/api/v1/notifications" \
    -H "Authorization: Bearer $CUSTOMER_TOKEN")

if echo "$NOTIF_RESP" | grep -q "OrderConfirmed"; then
    echo "PASS: Order confirmed and notification recorded successfully."
    exit 0
else
    echo "FAIL: Notification for OrderConfirmed not found in response."
    exit 1
fi
