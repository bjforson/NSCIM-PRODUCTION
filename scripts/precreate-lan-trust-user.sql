-- Pre-create the LAN-trust shared identity. Idempotent.
INSERT INTO identity.users (internal_user_id, cf_access_sub, email, display_name, status, created_at, last_seen_at, tenant_id)
VALUES (gen_random_uuid(), 'lan-trust:lan-trust@nickscan.com', 'lan-trust@nickscan.com', 'LAN Trust (shared)', 0, now(), now(), 1)
ON CONFLICT DO NOTHING;

SELECT internal_user_id, email, display_name, status, tenant_id
  FROM identity.users
 WHERE email = 'lan-trust@nickscan.com';
