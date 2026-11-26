begin;
alter table uno.matches drop constraint if exists matches_match_mode_check;
alter table uno.matches add constraint matches_match_mode_check check (match_mode in ('rooms','bots','ranked','legacy'));
-- Existing rooms cannot be distinguished reliably from genuine private matches.
-- Preserve all existing classifications unless an operator supplies a verified cutoff.
create table if not exists uno.runtime_checkpoint (
 id integer primary key check (id=1),
 state jsonb not null,
 updated_at timestamptz not null default now()
);
alter table uno.runtime_checkpoint enable row level security;
revoke all on uno.runtime_checkpoint from anon, authenticated, public;
commit;
