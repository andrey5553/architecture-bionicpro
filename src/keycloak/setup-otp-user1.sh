#!/bin/bash

echo "Waiting for Keycloak to start..."
sleep 10

/opt/keycloak/bin/kcadm.sh config credentials \
  --server http://keycloak:8080 \
  --realm master \
  --user admin \
  --password admin

USER_ID=$(/opt/keycloak/bin/kcadm.sh get users -r reports-realm -q username=user1 --fields id | grep -oE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')

echo "User1 ID: $USER_ID"

if [ -n "$USER_ID" ]; then
  /opt/keycloak/bin/kcadm.sh update users/$USER_ID -r reports-realm \
    -s 'requiredActions=["CONFIGURE_TOTP"]'
  echo "OTP required action set for user1"
else
  echo "ERROR: Could not find user1"
fi

echo "Setup completed!"
