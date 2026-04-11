#!/bin/bash

echo 'Waiting 60 seconds for Keycloak to start...'
sleep 60

echo 'Configuring credentials...'
/opt/keycloak/bin/kcadm.sh config credentials --server http://keycloak:8080 --realm master --user admin --password admin

echo 'Importing realm...'
/opt/keycloak/bin/kcadm.sh create realms -f /tmp/realm-clean.json 2>/dev/null || /opt/keycloak/bin/kcadm.sh update realms/reports-realm -f /tmp/realm-clean.json

echo 'Creating LDAP provider...'
/opt/keycloak/bin/kcadm.sh create components -r reports-realm -f /tmp/ldap-component.json

echo 'Getting component ID...'
COMPONENT_ID=$(/opt/keycloak/bin/kcadm.sh get components -r reports-realm --query name=openldap --fields id | grep -oE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' | head -1)

echo "Component ID: $COMPONENT_ID"

if [ -n "$COMPONENT_ID" ]; then
  echo 'Creating role mapper...'
  sed "s/PARENT_ID_PLACEHOLDER/$COMPONENT_ID/g" /tmp/ldap-role-mapper.json > /tmp/ldap-role-mapper-final.json
  /opt/keycloak/bin/kcadm.sh create components -r reports-realm -f /tmp/ldap-role-mapper-final.json
  echo 'LDAP configuration completed successfully!'
else
  echo 'WARNING: Could not get component ID'
fi

# Настраиваем OTP только для user1
echo 'Configuring OTP for user1...'
/tmp/setup-otp-user1.sh

echo 'Import finished!'
exit 0
