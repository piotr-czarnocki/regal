#!/bin/sh
# Creates regal_app, the restricted role used by the running application.
# Tables are created by the owner role; the default privileges below apply to them.
set -eu

psql -v ON_ERROR_STOP=1 -v app_password="$REGAL_APP_PASSWORD" \
     --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
CREATE ROLE regal_app LOGIN PASSWORD :'app_password';
GRANT USAGE ON SCHEMA public TO regal_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO regal_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO regal_app;
SQL
