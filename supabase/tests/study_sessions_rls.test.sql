begin;

create extension if not exists pgtap with schema extensions;
select plan(14);

select has_table('public', 'study_sessions', 'study_sessions exists');
select ok(
    (select relrowsecurity and relforcerowsecurity from pg_class where oid = 'public.study_sessions'::regclass),
    'RLS is enabled and forced');
select policies_are(
    'public',
    'study_sessions',
    array['study_sessions_insert_own', 'study_sessions_select_own'],
    'only own-row SELECT and INSERT policies exist');
select ok(has_table_privilege('authenticated', 'public.study_sessions', 'SELECT'), 'authenticated can select');
select ok(has_table_privilege('authenticated', 'public.study_sessions', 'INSERT'), 'authenticated can insert');
select ok(not has_table_privilege('authenticated', 'public.study_sessions', 'UPDATE'), 'authenticated cannot update');
select ok(not has_table_privilege('authenticated', 'public.study_sessions', 'DELETE'), 'authenticated cannot delete');
select ok(not has_table_privilege('anon', 'public.study_sessions', 'SELECT'), 'anon cannot read sessions');
select ok(not has_table_privilege('authenticated', 'public.telegram_private_recipients', 'SELECT'), 'recipient cache is private');
select ok(not has_table_privilege('authenticated', 'public.telegram_report_deliveries', 'SELECT'), 'delivery ledger is private');
select ok(
    not has_function_privilege('authenticated', 'public.claim_telegram_report(uuid, uuid, uuid)', 'EXECUTE'),
    'clients cannot claim Telegram delivery locks');

insert into auth.users (id, aud, role, email, encrypted_password, email_confirmed_at, created_at, updated_at)
values
    ('11111111-1111-4111-8111-111111111111', 'authenticated', 'authenticated', 'one@example.invalid', '', now(), now(), now()),
    ('22222222-2222-4222-8222-222222222222', 'authenticated', 'authenticated', 'two@example.invalid', '', now(), now(), now());

set local role authenticated;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);

select lives_ok($test$
    insert into public.study_sessions (
        session_id, device_id, device_kind, mode, started_at, completed_at,
        outcome, bank_version, bank_sha256, rules_profile, schema_version,
        payload, payload_sha256)
    values (
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        'aaaaaaaa-0000-4000-8000-000000000001',
        'WindowsDesktop', 'Exam', now() - interval '1 minute', now(),
        'Passed', 'test-ab', repeat('A', 64), 'test-rules', 1,
        '{"sessionId":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"}'::jsonb,
        repeat('B', 64))
$test$, 'a user can insert an own session');

select set_config('request.jwt.claim.sub', '22222222-2222-4222-8222-222222222222', true);
select is(
    (select count(*) from public.study_sessions where session_id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
    0::bigint,
    'another user cannot read the session');
select throws_ok($test$
    insert into public.study_sessions (
        session_id, user_id, device_id, device_kind, mode, started_at, completed_at,
        outcome, bank_version, bank_sha256, rules_profile, schema_version,
        payload, payload_sha256)
    values (
        'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        '11111111-1111-4111-8111-111111111111',
        'bbbbbbbb-0000-4000-8000-000000000001',
        'MobilePwa', 'Exam', now() - interval '1 minute', now(),
        'Failed', 'test-ab', repeat('A', 64), 'test-rules', 1,
        '{"sessionId":"bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"}'::jsonb,
        repeat('C', 64))
$test$, '42501', 'new row violates row-level security policy for table "study_sessions"', 'a user cannot insert for another user');

select * from finish();
rollback;
