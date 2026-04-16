docker exec -it src-keycloak-1 bash -c "
/opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin &&
/opt/keycloak/bin/kcadm.sh get components -r reports-realm
"