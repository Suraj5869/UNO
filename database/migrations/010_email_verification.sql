begin;
alter table uno.users add column if not exists email_verified_at timestamptz;
create table if not exists uno.email_verification_tokens (
    user_id uuid primary key references uno.users(id) on delete cascade,
    token_hash text not null unique,
    expires_at timestamptz not null,
    created_at timestamptz not null default now()
);
alter table uno.email_verification_tokens enable row level security;
revoke all on uno.email_verification_tokens from public, anon, authenticated;
grant select, insert, update, delete on uno.email_verification_tokens to uno_runtime;
drop policy if exists runtime_email_verification on uno.email_verification_tokens;
create policy runtime_email_verification on uno.email_verification_tokens for all to uno_runtime using (true) with check (true);
commit;
