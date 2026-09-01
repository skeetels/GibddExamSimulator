begin;

create table if not exists public.telegram_private_recipients (
    recipient_key text primary key,
    username text not null,
    chat_id_text text not null,
    confirmed_at timestamptz not null default now(),
    constraint ck_telegram_private_recipient_fixed check (
        recipient_key = 'skeetels' and lower(username) = 'skeetels'),
    constraint ck_telegram_private_recipient_chat_id check (
        chat_id_text ~ '^-?[0-9]{1,20}$')
);

create table if not exists public.telegram_report_deliveries (
    session_id uuid primary key references public.study_sessions(session_id) on delete cascade,
    user_id uuid not null references auth.users(id) on delete cascade,
    status text not null default 'pending',
    attempt_count integer not null default 0,
    lock_token uuid,
    locked_until timestamptz,
    last_error text,
    telegram_message_id bigint,
    sent_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_telegram_report_status check (status in ('pending', 'sending', 'failed', 'sent')),
    constraint ck_telegram_report_attempt_count check (attempt_count >= 0),
    constraint ck_telegram_report_error_length check (length(coalesce(last_error, '')) <= 2000)
);

alter table public.telegram_private_recipients enable row level security;
alter table public.telegram_private_recipients force row level security;
alter table public.telegram_report_deliveries enable row level security;
alter table public.telegram_report_deliveries force row level security;

revoke all on table public.telegram_private_recipients from anon, authenticated;
revoke all on table public.telegram_report_deliveries from anon, authenticated;

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
begin
    if not exists (
        select 1
          from public.study_sessions
         where session_id = p_session_id
           and user_id = p_user_id
    ) then
        raise exception 'Study session does not belong to the requested user.'
            using errcode = '42501';
    end if;

    insert into public.telegram_report_deliveries(session_id, user_id)
    values (p_session_id, p_user_id)
    on conflict (session_id) do nothing;

    update public.telegram_report_deliveries
       set status = 'sending',
           attempt_count = attempt_count + 1,
           lock_token = p_lock_token,
           locked_until = now() + interval '5 minutes',
           last_error = null,
           updated_at = now()
     where session_id = p_session_id
       and user_id = p_user_id
       and status <> 'sent'
       and (status <> 'sending' or locked_until is null or locked_until < now());

    if found then
        return 'claimed';
    end if;

    select status
      into current_status
      from public.telegram_report_deliveries
     where session_id = p_session_id
       and user_id = p_user_id;

    if current_status = 'sent' then
        return 'sent';
    end if;
    return 'busy';
end;
$$;

revoke all on function public.claim_telegram_report(uuid, uuid, uuid) from public, anon, authenticated;
grant execute on function public.claim_telegram_report(uuid, uuid, uuid) to service_role;

comment on table public.telegram_private_recipients is
    'Private server-side cache for the one fixed Telegram recipient. Never exposed through the client API.';
comment on table public.telegram_report_deliveries is
    'Idempotent server-side Telegram delivery ledger keyed by immutable study session.';

commit;
