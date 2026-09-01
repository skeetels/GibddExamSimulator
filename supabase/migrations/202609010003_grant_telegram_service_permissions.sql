begin;

-- Edge Functions authenticate as service_role. Keep private Telegram state
-- inaccessible to clients while granting the server the least privileges it
-- needs to persist the owner chat, build reports and drain the retry queue.
grant select on table public.study_sessions to service_role;
grant select, update on table public.learning_profiles, public.device_memberships to service_role;
grant select, insert, update on table
    public.telegram_private_recipients,
    public.telegram_report_deliveries,
    public.telegram_profile_links,
    public.telegram_link_tokens
to service_role;

commit;
