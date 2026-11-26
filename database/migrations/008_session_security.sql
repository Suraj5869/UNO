begin;
create table if not exists uno.auth_sessions (
 id uuid primary key,
 user_id uuid not null references uno.users(id) on delete cascade,
 expires_at timestamptz not null,
 revoked_at timestamptz
);
alter table uno.refresh_tokens add column if not exists session_id uuid references uno.auth_sessions(id) on delete cascade;
create index if not exists refresh_tokens_session_idx on uno.refresh_tokens(session_id);
-- Existing tokens cannot be assigned a trustworthy session family. Require fresh login.
update uno.refresh_tokens set revoked_at=now() where session_id is null and revoked_at is null;
create table if not exists uno.login_attempts (
 identifier_hash text primary key,
 attempts integer not null,
 window_start timestamptz not null
);
alter table uno.auth_sessions enable row level security;
alter table uno.login_attempts enable row level security;
revoke all on uno.auth_sessions,uno.login_attempts from public,anon,authenticated;
commit;
