begin;

create extension if not exists pgcrypto;

create table if not exists public.learning_profiles (
    id uuid primary key default gen_random_uuid(),
    created_at timestamptz not null default now(),
    bank_version text not null default 'ab-2025-05-26',
    latest_revision bigint not null default 0
);

create table if not exists public.device_memberships (
    id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references public.learning_profiles(id) on delete cascade,
    auth_user_id uuid not null references auth.users(id) on delete cascade,
    device_id uuid not null,
    platform text not null,
    device_name text not null,
    created_at timestamptz not null default now(),
    last_seen_at timestamptz not null default now(),
    revoked_at timestamptz null,
    constraint ck_device_membership_platform check (platform in (
        'WindowsDesktop', 'MobilePwa', 'AndroidApp')),
    constraint ck_device_membership_name check (length(device_name) between 1 and 120)
);

create unique index if not exists ux_device_membership_active_device
    on public.device_memberships(auth_user_id, device_id)
    where revoked_at is null;
create index if not exists ix_device_membership_profile_active
    on public.device_memberships(profile_id, last_seen_at desc)
    where revoked_at is null;

create table if not exists public.pairing_requests (
    id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references public.learning_profiles(id) on delete cascade,
    created_by_auth_user_id uuid not null references auth.users(id) on delete cascade,
    created_by_device_id uuid not null,
    secret_hash text not null,
    short_code_hash text not null,
    expires_at timestamptz not null,
    consumed_at timestamptz null,
    consumed_by_auth_user_id uuid null references auth.users(id) on delete set null,
    status text not null default 'pending',
    failed_attempts integer not null default 0,
    locked_until timestamptz null,
    status_window_started_at timestamptz not null default now(),
    status_poll_count integer not null default 0,
    created_at timestamptz not null default now(),
    constraint ck_pairing_secret_hash check (secret_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_pairing_short_hash check (short_code_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_pairing_status check (status in ('pending', 'completed', 'expired', 'cancelled')),
    constraint ck_pairing_ttl check (expires_at > created_at and expires_at <= created_at + interval '10 minutes'),
    constraint ck_pairing_attempts check (failed_attempts between 0 and 20),
    constraint ck_pairing_status_polls check (status_poll_count between 0 and 300)
);

create unique index if not exists ux_pairing_one_pending_per_device
    on public.pairing_requests(created_by_auth_user_id, created_by_device_id)
    where status = 'pending';
create index if not exists ix_pairing_short_pending
    on public.pairing_requests(short_code_hash, expires_at)
    where status = 'pending';

create table if not exists public.pairing_completion_limits (
    auth_user_id uuid primary key references auth.users(id) on delete cascade,
    window_started_at timestamptz not null default now(),
    attempt_count integer not null default 0,
    locked_until timestamptz null,
    constraint ck_pairing_completion_attempts check (attempt_count between 0 and 100)
);

create table if not exists public.telegram_profile_links (
    profile_id uuid primary key references public.learning_profiles(id) on delete cascade,
    telegram_chat_id bigint not null,
    telegram_username text null,
    linked_at timestamptz not null default now(),
    revoked_at timestamptz null
);

create table if not exists public.telegram_link_tokens (
    id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references public.learning_profiles(id) on delete cascade,
    token_hash text not null unique,
    expires_at timestamptz not null,
    consumed_at timestamptz null,
    created_at timestamptz not null default now(),
    constraint ck_telegram_link_hash check (token_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_telegram_link_ttl check (expires_at > created_at and expires_at <= created_at + interval '15 minutes')
);

alter table public.study_sessions add column if not exists profile_id uuid;

insert into public.learning_profiles(id, created_at, bank_version)
select distinct user_id, min(inserted_at), max(bank_version)
from public.study_sessions
where user_id is not null
group by user_id
on conflict (id) do nothing;

insert into public.device_memberships(
    profile_id, auth_user_id, device_id, platform, device_name, created_at, last_seen_at)
select user_id,
       user_id,
       device_id,
       device_kind,
       case device_kind
           when 'WindowsDesktop' then 'Компьютер'
           when 'AndroidApp' then 'Телефон'
           else 'Браузер'
       end,
       min(inserted_at),
       max(inserted_at)
from public.study_sessions
where user_id is not null
group by user_id, device_id, device_kind
on conflict do nothing;

update public.study_sessions
set profile_id = user_id
where profile_id is null and user_id is not null;

alter table public.study_sessions
    alter column profile_id set not null;
alter table public.study_sessions
    add constraint fk_study_sessions_profile
    foreign key (profile_id) references public.learning_profiles(id) on delete cascade;

create index if not exists ix_study_sessions_profile_seq
    on public.study_sessions(profile_id, server_seq);

create or replace function public.is_profile_member(requested_profile_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select exists (
        select 1
        from public.device_memberships membership
        where membership.profile_id = requested_profile_id
          and membership.auth_user_id = auth.uid()
          and membership.revoked_at is null
    );
$$;

create or replace function public.ensure_device_membership(
    requested_device_id uuid,
    requested_platform text,
    requested_device_name text)
returns table(profile_id uuid, has_peer_device boolean, telegram_linked boolean, latest_revision bigint)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    current_user_id uuid := auth.uid();
    selected_profile_id uuid;
begin
    if current_user_id is null then
        raise exception 'authentication required';
    end if;
    if requested_device_id is null or requested_platform not in ('WindowsDesktop', 'MobilePwa', 'AndroidApp') then
        raise exception 'invalid device';
    end if;

    select membership.profile_id
      into selected_profile_id
      from public.device_memberships membership
     where membership.auth_user_id = current_user_id
       and membership.device_id = requested_device_id
       and membership.revoked_at is null
     limit 1;

    if selected_profile_id is null then
        insert into public.learning_profiles default values returning id into selected_profile_id;
        insert into public.device_memberships(
            profile_id, auth_user_id, device_id, platform, device_name)
        values (
            selected_profile_id,
            current_user_id,
            requested_device_id,
            requested_platform,
            left(coalesce(nullif(trim(requested_device_name), ''), 'Устройство'), 120));
    else
        update public.device_memberships
           set last_seen_at = now(),
               platform = requested_platform,
               device_name = left(coalesce(nullif(trim(requested_device_name), ''), device_name), 120)
         where auth_user_id = current_user_id
           and device_id = requested_device_id
           and revoked_at is null;
    end if;

    return query
    select selected_profile_id,
           exists (
               select 1
                 from public.device_memberships peer
                where peer.profile_id = selected_profile_id
                  and peer.revoked_at is null
                  and peer.device_id <> requested_device_id),
           exists (
               select 1
                 from public.telegram_profile_links link
                where link.profile_id = selected_profile_id
                  and link.revoked_at is null),
           coalesce((
               select max(session.server_seq)
                 from public.study_sessions session
                where session.profile_id = selected_profile_id), 0);
end;
$$;

create or replace function public.start_device_pairing(
    requested_device_id uuid,
    requested_secret_hash text,
    requested_short_code_hash text,
    requested_expires_at timestamptz)
returns table(pairing_id uuid, profile_id uuid, expires_at timestamptz)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    current_user_id uuid := auth.uid();
    current_profile_id uuid;
    request_id uuid;
begin
    if current_user_id is null then
        raise exception 'authentication required';
    end if;
    if requested_expires_at <= now() or requested_expires_at > now() + interval '6 minutes' then
        raise exception 'invalid expiry';
    end if;
    if requested_secret_hash !~ '^[0-9a-f]{64}$' or requested_short_code_hash !~ '^[0-9a-f]{64}$' then
        raise exception 'invalid hash';
    end if;
    if (select count(*) from public.pairing_requests request
        where request.created_by_auth_user_id = current_user_id
          and request.created_at > now() - interval '1 minute') >= 6 then
        raise exception 'rate limit';
    end if;

    select membership.profile_id
      into current_profile_id
      from public.device_memberships membership
     where membership.auth_user_id = current_user_id
       and membership.device_id = requested_device_id
       and membership.revoked_at is null
     limit 1;
    if current_profile_id is null then
        raise exception 'device is not registered';
    end if;

    update public.pairing_requests
       set status = 'cancelled'
     where created_by_auth_user_id = current_user_id
       and created_by_device_id = requested_device_id
       and status = 'pending';

    insert into public.pairing_requests(
        profile_id, created_by_auth_user_id, created_by_device_id,
        secret_hash, short_code_hash, expires_at)
    values (
        current_profile_id, current_user_id, requested_device_id,
        requested_secret_hash, requested_short_code_hash, requested_expires_at)
    returning id into request_id;

    return query select request_id, current_profile_id, requested_expires_at;
end;
$$;

create or replace function public.read_device_pairing_status(
    requested_pairing_id uuid)
returns table(
    result_status text,
    linked_profile_id uuid,
    request_expires_at timestamptz,
    consumed_by_auth_user_id uuid)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    current_user_id uuid := auth.uid();
    target_request public.pairing_requests%rowtype;
begin
    if current_user_id is null then
        raise exception 'authentication required';
    end if;

    select * into target_request
      from public.pairing_requests request
     where request.id = requested_pairing_id
       and request.created_by_auth_user_id = current_user_id
     for update;
    if target_request.id is null then
        return query select 'not_found'::text, null::uuid, null::timestamptz, null::uuid;
        return;
    end if;

    if target_request.status_window_started_at < now() - interval '10 minutes' then
        update public.pairing_requests
           set status_window_started_at = now(), status_poll_count = 0
         where id = target_request.id
        returning * into target_request;
    end if;
    if target_request.status_poll_count >= 150 then
        return query select 'rate_limited'::text, null::uuid,
            target_request.expires_at, null::uuid;
        return;
    end if;
    update public.pairing_requests
       set status_poll_count = status_poll_count + 1
     where id = target_request.id;

    if target_request.status = 'pending' and target_request.expires_at <= now() then
        update public.pairing_requests
           set status = 'expired'
         where id = target_request.id and status = 'pending';
        target_request.status := 'expired';
    end if;

    return query select target_request.status,
        case when target_request.status = 'completed' then target_request.profile_id else null::uuid end,
        target_request.expires_at,
        case when target_request.status = 'completed' then target_request.consumed_by_auth_user_id else null::uuid end;
end;
$$;

create or replace function public.complete_device_pairing(
    requested_pairing_id uuid,
    requested_secret_hash text,
    requested_short_code_hash text,
    requested_device_id uuid,
    requested_platform text,
    requested_device_name text)
returns table(result_status text, linked_profile_id uuid, latest_revision bigint)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    current_user_id uuid := auth.uid();
    target_request public.pairing_requests%rowtype;
    source_profile_id uuid;
    completion_limit public.pairing_completion_limits%rowtype;
begin
    if current_user_id is null then
        raise exception 'authentication required';
    end if;

    insert into public.pairing_completion_limits(auth_user_id)
    values (current_user_id)
    on conflict (auth_user_id) do nothing;
    select * into completion_limit
      from public.pairing_completion_limits
     where auth_user_id = current_user_id
     for update;
    if completion_limit.window_started_at < now() - interval '10 minutes' then
        update public.pairing_completion_limits
           set window_started_at = now(), attempt_count = 0, locked_until = null
         where auth_user_id = current_user_id
        returning * into completion_limit;
    end if;
    if completion_limit.locked_until is not null and completion_limit.locked_until > now() then
        return query select 'rate_limited'::text, null::uuid, 0::bigint;
        return;
    end if;
    update public.pairing_completion_limits
       set attempt_count = least(100, attempt_count + 1),
           locked_until = case when attempt_count + 1 >= 20 then now() + interval '5 minutes' else locked_until end
     where auth_user_id = current_user_id;

    if requested_pairing_id is not null then
        select * into target_request
          from public.pairing_requests request
         where request.id = requested_pairing_id
         for update;
    else
        select * into target_request
          from public.pairing_requests request
         where request.short_code_hash = requested_short_code_hash
           and request.status = 'pending'
         order by request.created_at desc
         limit 1
         for update;
    end if;

    if target_request.id is null then
        return query select 'invalid'::text, null::uuid, 0::bigint;
        return;
    end if;
    if target_request.status <> 'pending' then
        return query select 'replayed'::text, null::uuid, 0::bigint;
        return;
    end if;
    if target_request.expires_at <= now() then
        update public.pairing_requests set status = 'expired' where id = target_request.id;
        return query select 'expired'::text, null::uuid, 0::bigint;
        return;
    end if;
    if target_request.locked_until is not null and target_request.locked_until > now() then
        return query select 'rate_limited'::text, null::uuid, 0::bigint;
        return;
    end if;
    if (requested_pairing_id is not null and target_request.secret_hash <> requested_secret_hash)
       or (requested_pairing_id is null and target_request.short_code_hash <> requested_short_code_hash) then
        update public.pairing_requests
           set failed_attempts = least(20, failed_attempts + 1),
               locked_until = case when failed_attempts + 1 >= 5 then now() + interval '2 minutes' else locked_until end
         where id = target_request.id;
        return query select 'invalid'::text, null::uuid, 0::bigint;
        return;
    end if;
    if target_request.created_by_auth_user_id = current_user_id then
        return query select 'same_device'::text, null::uuid, 0::bigint;
        return;
    end if;

    select membership.profile_id
      into source_profile_id
      from public.device_memberships membership
     where membership.auth_user_id = current_user_id
       and membership.device_id = requested_device_id
       and membership.revoked_at is null
     limit 1;

    if source_profile_id is not null and source_profile_id <> target_request.profile_id then
        if (select count(*) from public.device_memberships membership
            where membership.profile_id = source_profile_id and membership.revoked_at is null) = 1 then
            update public.study_sessions
               set profile_id = target_request.profile_id
             where profile_id = source_profile_id
               and not exists (
                   select 1 from public.study_sessions duplicate
                   where duplicate.profile_id = target_request.profile_id
                     and duplicate.session_id = public.study_sessions.session_id);
        end if;
        update public.device_memberships
           set revoked_at = now()
         where auth_user_id = current_user_id
           and device_id = requested_device_id
           and revoked_at is null;
    end if;

    if source_profile_id = target_request.profile_id then
        update public.device_memberships
           set platform = requested_platform,
               device_name = left(coalesce(nullif(trim(requested_device_name), ''), 'Телефон'), 120),
               last_seen_at = now()
         where auth_user_id = current_user_id
           and device_id = requested_device_id
           and profile_id = target_request.profile_id
           and revoked_at is null;
    else
        insert into public.device_memberships(
            profile_id, auth_user_id, device_id, platform, device_name)
        values (
            target_request.profile_id,
            current_user_id,
            requested_device_id,
            requested_platform,
            left(coalesce(nullif(trim(requested_device_name), ''), 'Телефон'), 120));
    end if;

    update public.pairing_requests
       set status = 'completed',
           consumed_at = now(),
           consumed_by_auth_user_id = current_user_id
     where id = target_request.id and status = 'pending';

    update public.pairing_completion_limits
       set attempt_count = 0, window_started_at = now(), locked_until = null
     where auth_user_id = current_user_id;

    return query
    select 'completed'::text,
           target_request.profile_id,
           coalesce((select max(session.server_seq)
                       from public.study_sessions session
                      where session.profile_id = target_request.profile_id), 0);
end;
$$;

drop policy if exists study_sessions_select_own on public.study_sessions;
drop policy if exists study_sessions_insert_own on public.study_sessions;
create policy study_sessions_select_profile
    on public.study_sessions
    for select
    to authenticated
    using (public.is_profile_member(profile_id));
create policy study_sessions_insert_profile
    on public.study_sessions
    for insert
    to authenticated
    with check (public.is_profile_member(profile_id) and user_id = auth.uid());

alter table public.learning_profiles enable row level security;
alter table public.learning_profiles force row level security;
alter table public.device_memberships enable row level security;
alter table public.device_memberships force row level security;
alter table public.pairing_requests enable row level security;
alter table public.pairing_requests force row level security;
alter table public.pairing_completion_limits enable row level security;
alter table public.pairing_completion_limits force row level security;
alter table public.telegram_profile_links enable row level security;
alter table public.telegram_profile_links force row level security;
alter table public.telegram_link_tokens enable row level security;
alter table public.telegram_link_tokens force row level security;

create policy learning_profiles_select_member on public.learning_profiles
    for select to authenticated using (public.is_profile_member(id));
create policy device_memberships_select_member on public.device_memberships
    for select to authenticated using (public.is_profile_member(profile_id));

revoke all on public.learning_profiles, public.device_memberships,
    public.pairing_requests, public.pairing_completion_limits, public.telegram_profile_links,
    public.telegram_link_tokens from anon, authenticated;
grant select on public.learning_profiles, public.device_memberships to authenticated;
revoke all on function public.is_profile_member(uuid) from public, anon, authenticated;
revoke all on function public.ensure_device_membership(uuid, text, text) from public, anon, authenticated;
revoke all on function public.start_device_pairing(uuid, text, text, timestamptz) from public, anon, authenticated;
revoke all on function public.read_device_pairing_status(uuid) from public, anon, authenticated;
revoke all on function public.complete_device_pairing(uuid, text, text, uuid, text, text) from public, anon, authenticated;
grant execute on function public.is_profile_member(uuid) to authenticated;
grant execute on function public.ensure_device_membership(uuid, text, text) to authenticated;
grant execute on function public.start_device_pairing(uuid, text, text, timestamptz) to authenticated;
grant execute on function public.read_device_pairing_status(uuid) to authenticated;
grant execute on function public.complete_device_pairing(uuid, text, text, uuid, text, text) to authenticated;

alter table public.telegram_report_deliveries add column if not exists profile_id uuid;
update public.telegram_report_deliveries delivery
   set profile_id = session.profile_id
  from public.study_sessions session
 where session.session_id = delivery.session_id
   and delivery.profile_id is null;
alter table public.telegram_report_deliveries alter column profile_id set not null;
alter table public.telegram_report_deliveries
    add constraint fk_telegram_delivery_profile
    foreign key (profile_id) references public.learning_profiles(id) on delete cascade;

create or replace function public.claim_telegram_report(
    p_session_id uuid,
    p_user_id uuid,
    p_lock_token uuid)
returns text
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    current_status text;
    session_profile_id uuid;
begin
    select profile_id into session_profile_id
      from public.study_sessions
     where session_id = p_session_id
       and user_id = p_user_id;
    if session_profile_id is null then
        raise exception 'Study session is unavailable.' using errcode = '42501';
    end if;

    insert into public.telegram_report_deliveries(session_id, user_id, profile_id)
    values (p_session_id, p_user_id, session_profile_id)
    on conflict (session_id) do nothing;

    update public.telegram_report_deliveries
       set status = 'sending',
           attempt_count = attempt_count + 1,
           lock_token = p_lock_token,
           locked_until = now() + interval '5 minutes',
           last_error = null,
           updated_at = now()
     where session_id = p_session_id
       and status <> 'sent'
       and (status <> 'sending' or locked_until is null or locked_until < now());
    if found then return 'claimed'; end if;

    select status into current_status
      from public.telegram_report_deliveries
     where session_id = p_session_id;
    if current_status = 'sent' then return 'sent'; end if;
    return 'busy';
end;
$$;

create or replace function public.queue_exam_telegram_report()
returns trigger
language plpgsql
security definer
set search_path = public, pg_temp
as $$
begin
    if new.mode = 'Exam' then
        insert into public.telegram_report_deliveries(session_id, user_id, profile_id)
        values (new.session_id, new.user_id, new.profile_id)
        on conflict (session_id) do nothing;
    end if;
    return new;
end;
$$;

drop trigger if exists trg_queue_exam_telegram_report on public.study_sessions;
create trigger trg_queue_exam_telegram_report
after insert on public.study_sessions
for each row execute function public.queue_exam_telegram_report();

revoke all on function public.queue_exam_telegram_report() from public, anon, authenticated;
revoke all on function public.claim_telegram_report(uuid, uuid, uuid) from public, anon, authenticated;
grant execute on function public.claim_telegram_report(uuid, uuid, uuid) to service_role;

comment on table public.pairing_requests is
    'Five-minute one-time device invitations. Only SHA-256 hashes are stored.';
comment on function public.complete_device_pairing is
    'Atomically consumes one invitation and moves the anonymous device into the inviter profile.';

commit;
