begin;

create extension if not exists pgtap with schema extensions;
select plan(19);

insert into auth.users (id, aud, role, email, encrypted_password, email_confirmed_at, created_at, updated_at)
values
    ('44444444-4444-4444-8444-444444444444', 'authenticated', 'authenticated', null, '', now(), now(), now()),
    ('55555555-5555-4555-8555-555555555555', 'authenticated', 'authenticated', null, '', now(), now(), now());

set local role authenticated;
select set_config('request.jwt.claim.sub', '44444444-4444-4444-8444-444444444444', true);
select set_config(
    'test.owner_profile',
    (select profile_id::text from public.ensure_device_membership(
        '44444444-0000-4000-8000-000000000001', 'WindowsDesktop', 'Компьютер')),
    true);
select ok(current_setting('test.owner_profile')::uuid is not null, 'desktop silently receives a profile');

select set_config(
    'test.first_pairing',
    (select pairing_id::text from public.start_device_pairing(
        '44444444-0000-4000-8000-000000000001', repeat('a', 64), repeat('b', 64), now() + interval '5 minutes')),
    true);
select ok(current_setting('test.first_pairing')::uuid is not null, 'desktop creates a pairing request');
select is(
    (select result_status from public.read_device_pairing_status(
        current_setting('test.first_pairing')::uuid)),
    'pending',
    'the desktop can poll only its own pending invitation');

select set_config('request.jwt.claim.sub', '55555555-5555-4555-8555-555555555555', true);
select set_config(
    'test.phone_profile',
    (select profile_id::text from public.ensure_device_membership(
        '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон')),
    true);
select isnt(
    current_setting('test.phone_profile')::uuid,
    current_setting('test.owner_profile')::uuid,
    'a clean phone starts with an isolated anonymous profile');

select is(
    (select result_status from public.complete_device_pairing(
        current_setting('test.first_pairing')::uuid, repeat('a', 64), '',
        '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон')),
    'completed',
    'valid one-time secret links the phone');

reset role;
select is(
    (select status from public.pairing_requests where id = current_setting('test.first_pairing')::uuid),
    'completed',
    'the request is atomically consumed');
select is(
    (select count(*) from public.device_memberships
      where profile_id = current_setting('test.owner_profile')::uuid and revoked_at is null),
    2::bigint,
    'both devices are members of one profile');
select ok(
    not exists (select 1 from public.pairing_requests where secret_hash = 'temporary_secret'),
    'the one-time secret itself is never stored');

set local role authenticated;
select set_config('request.jwt.claim.sub', '55555555-5555-4555-8555-555555555555', true);
select is(
    (select result_status from public.complete_device_pairing(
        current_setting('test.first_pairing')::uuid, repeat('a', 64), '',
        '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон')),
    'replayed',
    'a consumed QR cannot be replayed');

select set_config('request.jwt.claim.sub', '44444444-4444-4444-8444-444444444444', true);
select set_config(
    'test.replaced_pairing',
    (select pairing_id::text from public.start_device_pairing(
        '44444444-0000-4000-8000-000000000001', repeat('c', 64), repeat('d', 64), now() + interval '5 minutes')),
    true);
select set_config(
    'test.current_pairing',
    (select pairing_id::text from public.start_device_pairing(
        '44444444-0000-4000-8000-000000000001', repeat('e', 64), repeat('f', 64), now() + interval '5 minutes')),
    true);

reset role;
select is(
    (select status from public.pairing_requests where id = current_setting('test.replaced_pairing')::uuid),
    'cancelled',
    'creating a new QR invalidates the previous pending QR');
select is(
    (select status from public.pairing_requests where id = current_setting('test.current_pairing')::uuid),
    'pending',
    'the newest QR remains usable');

update public.pairing_requests
   set created_at = now() - interval '6 minutes',
       expires_at = now() - interval '1 minute'
 where id = current_setting('test.current_pairing')::uuid;

set local role authenticated;
select set_config('request.jwt.claim.sub', '55555555-5555-4555-8555-555555555555', true);
select is(
    (select result_status from public.complete_device_pairing(
        current_setting('test.current_pairing')::uuid, repeat('e', 64), '',
        '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон')),
    'expired',
    'an expired QR is rejected');

select set_config('request.jwt.claim.sub', '44444444-4444-4444-8444-444444444444', true);
select set_config(
    'test.invalid_pairing',
    (select pairing_id::text from public.start_device_pairing(
        '44444444-0000-4000-8000-000000000001', repeat('1', 64), repeat('2', 64), now() + interval '5 minutes')),
    true);

select set_config('request.jwt.claim.sub', '55555555-5555-4555-8555-555555555555', true);
select is(
    (select result_status from public.complete_device_pairing(
        current_setting('test.invalid_pairing')::uuid, repeat('9', 64), '',
        '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон')),
    'invalid',
    'an incorrect secret is rejected');

reset role;
select is(
    (select failed_attempts from public.pairing_requests where id = current_setting('test.invalid_pairing')::uuid),
    1,
    'incorrect secrets are counted');

update public.pairing_requests
   set status_poll_count = 150
 where id = current_setting('test.invalid_pairing')::uuid;

set local role authenticated;
select set_config('request.jwt.claim.sub', '44444444-4444-4444-8444-444444444444', true);
select is(
    (select result_status from public.read_device_pairing_status(
        current_setting('test.invalid_pairing')::uuid)),
    'rate_limited',
    'pairing status polling has a durable per-invitation rate limit');

select set_config('request.jwt.claim.sub', '44444444-4444-4444-8444-444444444444', true);
select is(
    (select result_status from public.complete_device_pairing(
        current_setting('test.invalid_pairing')::uuid, repeat('1', 64), '',
        '44444444-0000-4000-8000-000000000001', 'WindowsDesktop', 'Компьютер')),
    'same_device',
    'a device cannot scan its own invitation');

reset role;
update public.pairing_completion_limits
   set attempt_count = 0, window_started_at = now(), locked_until = null
 where auth_user_id = '55555555-5555-4555-8555-555555555555';

set local role authenticated;
select set_config('request.jwt.claim.sub', '55555555-5555-4555-8555-555555555555', true);
do $$
begin
    for attempt in 1..20 loop
        perform * from public.complete_device_pairing(
            null, '', repeat('7', 64),
            '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон');
    end loop;
end;
$$;
select is(
    (select result_status from public.complete_device_pairing(
        null, '', repeat('7', 64),
        '55555555-0000-4000-8000-000000000001', 'AndroidApp', 'Телефон')),
    'rate_limited',
    'manual short-code guessing is rate limited');

reset role;
select ok(
    (select locked_until > now() from public.pairing_completion_limits
      where auth_user_id = '55555555-5555-4555-8555-555555555555'),
    'the completion throttle records a temporary lock');
select is(
    (select count(*) from public.pairing_requests where consumed_at is not null),
    1::bigint,
    'only one invitation was successfully consumed');

select * from finish();
rollback;
