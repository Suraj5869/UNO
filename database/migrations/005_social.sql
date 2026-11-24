begin;
create table if not exists uno.friendships (
 low_id uuid not null references uno.users(id) on delete cascade,
 high_id uuid not null references uno.users(id) on delete cascade,
 requester_id uuid not null references uno.users(id) on delete cascade,
 accepted boolean not null default false,
 created_at timestamptz not null default now(),
 primary key(low_id,high_id),
 check(low_id<high_id), check(requester_id in(low_id,high_id))
);
create table if not exists uno.social_presence (
 user_id uuid primary key references uno.users(id) on delete cascade,
 seen_at timestamptz not null default now()
);
create table if not exists uno.room_invitations (
 id uuid primary key default gen_random_uuid(),
 sender_id uuid not null references uno.users(id) on delete cascade,
 recipient_id uuid not null references uno.users(id) on delete cascade,
 room_code text not null,
 created_at timestamptz not null default now(),
 expires_at timestamptz not null default now()+interval '5 minutes',
 check(sender_id<>recipient_id)
);
create index if not exists social_invite_recipient on uno.room_invitations(recipient_id,expires_at);
alter table uno.friendships enable row level security;
alter table uno.social_presence enable row level security;
alter table uno.room_invitations enable row level security;
revoke all on uno.friendships,uno.social_presence,uno.room_invitations from public,anon,authenticated;
commit;
