begin;

alter table public.study_sessions
    drop constraint if exists ck_study_sessions_device_kind;

alter table public.study_sessions
    add constraint ck_study_sessions_device_kind check (device_kind in (
        'WindowsDesktop', 'MobilePwa', 'AndroidApp'));

comment on constraint ck_study_sessions_device_kind on public.study_sessions is
    'Shared session stream accepts Windows, installable Android, and browser PWA clients.';

commit;
