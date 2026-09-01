begin;

create extension if not exists pgtap with schema extensions;
select plan(34);

select has_table('public', 'study_sessions', 'study_sessions exists');
select has_table('public', 'learning_profiles', 'learning_profiles exists');
select has_table('public', 'device_memberships', 'device_memberships exists');
select has_table('public', 'pairing_requests', 'pairing_requests exists');
select ok(
    (select relrowsecurity and relforcerowsecurity from pg_class where oid = 'public.study_sessions'::regclass),
    'study session RLS is enabled and forced');
select ok(
    (select relrowsecurity and relforcerowsecurity from pg_class where oid = 'public.learning_profiles'::regclass),
    'profile RLS is enabled and forced');
select ok(
    (select relrowsecurity and relforcerowsecurity from pg_class where oid = 'public.device_memberships'::regclass),
    'membership RLS is enabled and forced');
select policies_are(
    'public',
    'study_sessions',
    array['study_sessions_insert_profile', 'study_sessions_select_profile'],
    'only append-only profile SELECT and INSERT policies exist');
select ok(has_table_privilege('authenticated', 'public.study_sessions', 'SELECT'), 'authenticated can select sessions');
select ok(has_table_privilege('authenticated', 'public.study_sessions', 'INSERT'), 'authenticated can insert sessions');
select ok(not has_table_privilege('authenticated', 'public.study_sessions', 'UPDATE'), 'authenticated cannot update sessions');
select ok(not has_table_privilege('authenticated', 'public.study_sessions', 'DELETE'), 'authenticated cannot delete sessions');
select ok(not has_table_privilege('anon', 'public.study_sessions', 'SELECT'), 'anon cannot read sessions');
select ok(not has_table_privilege('authenticated', 'public.pairing_requests', 'SELECT'), 'pairing requests are API-only');
select ok(not has_table_privilege('authenticated', 'public.telegram_profile_links', 'SELECT'), 'Telegram links are server-only');
select ok(
    not has_function_privilege('authenticated', 'public.claim_telegram_report(uuid, uuid, uuid)', 'EXECUTE'),
    'clients cannot claim Telegram delivery locks');
select ok(
    has_function_privilege('authenticated', 'public.ensure_device_membership(uuid, text, text)', 'EXECUTE'),
    'authenticated devices can bootstrap only through the RPC');
select ok(
    has_function_privilege('authenticated', 'public.complete_device_pairing(uuid, text, text, uuid, text, text)', 'EXECUTE'),
    'authenticated devices can complete pairing only through the atomic RPC');
select ok(
    has_function_privilege('authenticated', 'public.read_device_pairing_status(uuid)', 'EXECUTE'),
    'authenticated desktops poll pairing only through the rate-limited RPC');
select ok(
    not has_function_privilege('anon', 'public.read_device_pairing_status(uuid)', 'EXECUTE'),
    'unauthenticated callers cannot invoke pairing status');
select ok(
    has_table_privilege('service_role', 'public.study_sessions', 'SELECT'),
    'Telegram server can read report source sessions');
select ok(
    has_table_privilege('service_role', 'public.learning_profiles', 'UPDATE'),
    'device server can update profile revisions');
select ok(
    has_table_privilege('service_role', 'public.device_memberships', 'UPDATE'),
    'device server can revoke memberships');
select ok(
    has_table_privilege('service_role', 'public.telegram_private_recipients', 'SELECT')
    and has_table_privilege('service_role', 'public.telegram_private_recipients', 'INSERT')
    and has_table_privilege('service_role', 'public.telegram_private_recipients', 'UPDATE'),
    'Telegram server can persist the fixed recipient');
select ok(
    has_table_privilege('service_role', 'public.telegram_report_deliveries', 'SELECT')
    and has_table_privilege('service_role', 'public.telegram_report_deliveries', 'UPDATE'),
    'Telegram worker can drain the private delivery queue');
select ok(
    has_table_privilege('service_role', 'public.telegram_profile_links', 'SELECT')
    and has_table_privilege('service_role', 'public.telegram_profile_links', 'INSERT')
    and has_table_privilege('service_role', 'public.telegram_profile_links', 'UPDATE'),
    'Telegram bot can persist profile links');
select ok(
    has_table_privilege('service_role', 'public.telegram_link_tokens', 'SELECT')
    and has_table_privilege('service_role', 'public.telegram_link_tokens', 'INSERT')
    and has_table_privilege('service_role', 'public.telegram_link_tokens', 'UPDATE'),
    'Telegram server can create and consume one-time link tokens');

insert into auth.users (id, aud, role, email, encrypted_password, email_confirmed_at, created_at, updated_at)
values
    ('11111111-1111-4111-8111-111111111111', 'authenticated', 'authenticated', null, '', now(), now(), now()),
    ('22222222-2222-4222-8222-222222222222', 'authenticated', 'authenticated', null, '', now(), now(), now()),
    ('33333333-3333-4333-8333-333333333333', 'authenticated', 'authenticated', null, '', now(), now(), now());

insert into public.learning_profiles(id)
values
    ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
    ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb');

insert into public.device_memberships(profile_id, auth_user_id, device_id, platform, device_name)
values
    ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', '11111111-1111-4111-8111-111111111111',
     '11111111-0000-4000-8000-000000000001', 'WindowsDesktop', 'Компьютер'),
    ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', '22222222-2222-4222-8222-222222222222',
     '22222222-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон'),
    ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', '33333333-3333-4333-8333-333333333333',
     '33333333-0000-4000-8000-000000000001', 'MobilePwa', 'Браузер');

set local role authenticated;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);

select lives_ok($test$
    insert into public.study_sessions (
        session_id, user_id, profile_id, device_id, device_kind, mode, started_at, completed_at,
        outcome, bank_version, bank_sha256, rules_profile, schema_version,
        payload, payload_sha256)
    values (
        'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
        '11111111-1111-4111-8111-111111111111',
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        '11111111-0000-4000-8000-000000000001',
        'WindowsDesktop', 'Exam', now() - interval '1 minute', now(),
        'Passed', 'test-ab', repeat('A', 64), 'test-rules', 1,
        '{"sessionId":"cccccccc-cccc-4ccc-8ccc-cccccccccccc"}'::jsonb,
        repeat('B', 64))
$test$, 'a member can append an own session');

select set_config('request.jwt.claim.sub', '22222222-2222-4222-8222-222222222222', true);
select is(
    (select count(*) from public.study_sessions where session_id = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'),
    0::bigint,
    'another profile cannot read the session');
select is(
    (select count(*) from public.learning_profiles where id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
    0::bigint,
    'another profile cannot read profile metadata');
select is(
    (select count(*) from public.learning_profiles where id = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'),
    1::bigint,
    'a member can read its own profile metadata');
select throws_ok($test$
    insert into public.study_sessions (
        session_id, user_id, profile_id, device_id, device_kind, mode, started_at, completed_at,
        outcome, bank_version, bank_sha256, rules_profile, schema_version,
        payload, payload_sha256)
    values (
        'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
        '22222222-2222-4222-8222-222222222222',
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        '22222222-0000-4000-8000-000000000001',
        'AndroidApp', 'Exam', now() - interval '1 minute', now(),
        'Failed', 'test-ab', repeat('A', 64), 'test-rules', 1,
        '{"sessionId":"dddddddd-dddd-4ddd-8ddd-dddddddddddd"}'::jsonb,
        repeat('C', 64))
$test$, '42501', 'new row violates row-level security policy for table "study_sessions"',
    'a user cannot insert into another profile');

reset role;
update public.device_memberships
   set revoked_at = now()
 where auth_user_id = '11111111-1111-4111-8111-111111111111';

set local role authenticated;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);
select is(
    (select count(*) from public.study_sessions where session_id = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'),
    0::bigint,
    'revoking one device immediately removes its profile access');

select set_config('request.jwt.claim.sub', '33333333-3333-4333-8333-333333333333', true);
select is(
    (select count(*) from public.study_sessions where session_id = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'),
    1::bigint,
    'revoking one device does not revoke another member of the profile');

select * from finish();
rollback;
