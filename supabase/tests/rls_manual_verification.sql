-- Run in a disposable local Supabase project. Replace the UUIDs with two users
-- created specifically for this test, then remove those users after validation.
-- This script documents assertions; authenticated REST integration is exercised
-- by tests/GibddExamSimulator.Sync.Tests with a fake transport in normal CI.

select relrowsecurity, relforcerowsecurity
from pg_class
where oid = 'public.study_sessions'::regclass;

select grantee, privilege_type
from information_schema.role_table_grants
where table_schema = 'public' and table_name = 'study_sessions'
order by grantee, privilege_type;

select policyname, roles, cmd, qual, with_check
from pg_policies
where schemaname = 'public' and tablename = 'study_sessions'
order by policyname;

-- Expected:
-- * anon has no table grants;
-- * authenticated has SELECT and INSERT only;
-- * the two authenticated policies call is_profile_member(profile_id);
-- * no UPDATE or DELETE policy exists.
