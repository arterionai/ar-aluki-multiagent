-- 026_shelo_nabel_seed.sql
-- Creates the Sheló NABEL ORGANIZATION tenant and adds the authorized
-- WhatsApp numbers as members. Mexican mobile numbers can arrive from
-- Meta in two formats: 52XXXXXXXXXX (current) or 521XXXXXXXXX (legacy
-- pre-2019). Both variants for +52 55 2857 1249 are seeded so the user
-- is recognized regardless of which format Meta sends.
-- All inserts are idempotent (ON CONFLICT DO NOTHING).

DO $$
DECLARE
    v_tenant_id       uuid := 'c0c0c0c0-5e10-4000-a000-000000000001';
    v_user1_id        uuid;
    v_user2_id        uuid;
    v_user2_legacy_id uuid;
    v_ctx1_id         uuid;
    v_ctx2_id         uuid;
    v_ctx2_legacy_id  uuid;
BEGIN
    -- Tenant ------------------------------------------------------------------
    INSERT INTO tenants (id, tenant_type, display_name)
    VALUES (v_tenant_id, 'ORGANIZATION', 'Sheló NABEL')
    ON CONFLICT (id) DO NOTHING;

    -- User 1: Jaime (+1 425 230 7522) ----------------------------------------
    INSERT INTO users_profile (id, external_auth_id, phone)
    VALUES (gen_random_uuid(), '14252307522', '+14252307522')
    ON CONFLICT (external_auth_id) DO NOTHING;

    SELECT id INTO v_user1_id
    FROM users_profile WHERE external_auth_id = '14252307522';

    INSERT INTO memberships (tenant_id, user_id, role)
    VALUES (v_tenant_id, v_user1_id, 'OWNER')
    ON CONFLICT (tenant_id, user_id) DO NOTHING;

    INSERT INTO contexts (id, tenant_id, context_type, external_context_id, title)
    VALUES (gen_random_uuid(), v_tenant_id, 'DM', '14252307522', 'Jaime DM')
    ON CONFLICT (tenant_id, context_type, external_context_id) DO NOTHING;

    SELECT id INTO v_ctx1_id
    FROM contexts
    WHERE tenant_id = v_tenant_id AND context_type = 'DM' AND external_context_id = '14252307522';

    INSERT INTO context_access (context_id, user_id, access_role)
    VALUES (v_ctx1_id, v_user1_id, 'OWNER')
    ON CONFLICT (context_id, user_id) DO NOTHING;

    -- User 2: +52 55 2857 1249 — current Meta format (525528571249) ----------
    INSERT INTO users_profile (id, external_auth_id, phone)
    VALUES (gen_random_uuid(), '525528571249', '+525528571249')
    ON CONFLICT (external_auth_id) DO NOTHING;

    SELECT id INTO v_user2_id
    FROM users_profile WHERE external_auth_id = '525528571249';

    INSERT INTO memberships (tenant_id, user_id, role)
    VALUES (v_tenant_id, v_user2_id, 'MEMBER')
    ON CONFLICT (tenant_id, user_id) DO NOTHING;

    INSERT INTO contexts (id, tenant_id, context_type, external_context_id, title)
    VALUES (gen_random_uuid(), v_tenant_id, 'DM', '525528571249', 'Contacto DM')
    ON CONFLICT (tenant_id, context_type, external_context_id) DO NOTHING;

    SELECT id INTO v_ctx2_id
    FROM contexts
    WHERE tenant_id = v_tenant_id AND context_type = 'DM' AND external_context_id = '525528571249';

    INSERT INTO context_access (context_id, user_id, access_role)
    VALUES (v_ctx2_id, v_user2_id, 'OWNER')
    ON CONFLICT (context_id, user_id) DO NOTHING;

    -- User 2: +52 55 2857 1249 — legacy Meta format (5215528571249) ----------
    -- Same phone, different wa_id that Meta used before 2019. Seeded so the
    -- user is authorized regardless of which format arrives in the webhook.
    INSERT INTO users_profile (id, external_auth_id, phone)
    VALUES (gen_random_uuid(), '5215528571249', '+525528571249')
    ON CONFLICT (external_auth_id) DO NOTHING;

    SELECT id INTO v_user2_legacy_id
    FROM users_profile WHERE external_auth_id = '5215528571249';

    INSERT INTO memberships (tenant_id, user_id, role)
    VALUES (v_tenant_id, v_user2_legacy_id, 'MEMBER')
    ON CONFLICT (tenant_id, user_id) DO NOTHING;

    INSERT INTO contexts (id, tenant_id, context_type, external_context_id, title)
    VALUES (gen_random_uuid(), v_tenant_id, 'DM', '5215528571249', 'Contacto DM (legacy)')
    ON CONFLICT (tenant_id, context_type, external_context_id) DO NOTHING;

    SELECT id INTO v_ctx2_legacy_id
    FROM contexts
    WHERE tenant_id = v_tenant_id AND context_type = 'DM' AND external_context_id = '5215528571249';

    INSERT INTO context_access (context_id, user_id, access_role)
    VALUES (v_ctx2_legacy_id, v_user2_legacy_id, 'OWNER')
    ON CONFLICT (context_id, user_id) DO NOTHING;
END $$;
