begin;
do $$ begin
 if not exists(select 1 from pg_roles where rolname='uno_runtime') then
  create role uno_runtime nologin nosuperuser nocreatedb nocreaterole noinherit nobypassrls;
 end if;
end $$;
grant usage on schema uno to uno_runtime;
do $$
declare t text;
begin
 foreach t in array array['users','player_profiles','player_progress','player_statistics','player_inventory','player_loadouts','refresh_tokens','auth_sessions','login_attempts','rooms','room_members','matches','match_players','match_snapshots','xp_transactions','runtime_checkpoint','friendships','social_presence','live_room_invitations'] loop
  if to_regclass(format('uno.%I',t)) is null then raise exception 'Missing runtime table: %',t; end if;
  execute format('grant select,insert,update,delete on uno.%I to uno_runtime',t);
  execute format('alter table uno.%I enable row level security',t);
  execute format('drop policy if exists uno_runtime_access on uno.%I',t);
  execute format('create policy uno_runtime_access on uno.%I for all to uno_runtime using (true) with check (true)',t);
 end loop;
end $$;
grant select on uno.cosmetic_items to uno_runtime;
drop policy if exists uno_runtime_read on uno.cosmetic_items;
create policy uno_runtime_read on uno.cosmetic_items for select to uno_runtime using (true);
commit;
-- Provision a separate LOGIN role with a strong password outside source control,
-- grant uno_runtime to it with inheritance, and configure the API connection.
-- Do not use postgres, service_role, or the migration owner as the runtime login.
