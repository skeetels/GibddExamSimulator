begin;

create table if not exists public.study_sessions (
    server_seq bigint generated always as identity primary key,
    session_id uuid not null unique,
    user_id uuid not null default auth.uid() references auth.users(id) on delete cascade,
    device_id uuid not null,
    device_kind text not null,
    mode text not null,
    started_at timestamptz not null,
    completed_at timestamptz not null,
    outcome text not null,
    bank_version text not null,
    bank_sha256 text not null,
    rules_profile text not null,
    schema_version integer not null,
    payload jsonb not null,
    payload_sha256 text not null,
    inserted_at timestamptz not null default now(),
    constraint ck_study_sessions_time check (completed_at >= started_at),
    constraint ck_study_sessions_payload_object check (jsonb_typeof(payload) = 'object'),
    constraint ck_study_sessions_schema check (schema_version between 1 and 100),
    constraint ck_study_sessions_device_kind check (device_kind in (
        'WindowsDesktop', 'MobilePwa')),
    constraint ck_study_sessions_mode check (mode in (
        'Exam', 'SmartTen', 'MistakeReview', 'WeakTopics', 'Ticket',
        'Marathon', 'NoMistakeChallenge', 'LegacyImport')),
    constraint ck_study_sessions_outcome check (outcome in (
        'Passed', 'Failed', 'Completed', 'Abandoned')),
    constraint ck_study_sessions_payload_sha check (payload_sha256 ~ '^[0-9A-F]{64}$'),
    constraint ck_study_sessions_bank_sha check (bank_sha256 ~ '^[0-9A-F]{64}$')
);

create index if not exists ix_study_sessions_user_seq
    on public.study_sessions(user_id, server_seq);

alter table public.study_sessions enable row level security;
alter table public.study_sessions force row level security;

drop policy if exists study_sessions_select_own on public.study_sessions;
create policy study_sessions_select_own
    on public.study_sessions
    for select
    to authenticated
    using ((select auth.uid()) = user_id);

drop policy if exists study_sessions_insert_own on public.study_sessions;
create policy study_sessions_insert_own
    on public.study_sessions
    for insert
    to authenticated
    with check ((select auth.uid()) = user_id);

revoke all on table public.study_sessions from anon;
revoke all on table public.study_sessions from authenticated;
grant select, insert on table public.study_sessions to authenticated;

revoke all on sequence public.study_sessions_server_seq_seq from anon;
revoke all on sequence public.study_sessions_server_seq_seq from authenticated;
grant usage, select on sequence public.study_sessions_server_seq_seq to authenticated;

comment on table public.study_sessions is
    'Append-only completed study sessions. Client UPDATE and DELETE are intentionally forbidden.';
comment on column public.study_sessions.payload_sha256 is
    'SHA-256 of the canonical payload used for idempotency and integrity-conflict detection.';

commit;
