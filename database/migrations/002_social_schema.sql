begin;
alter table uno.player_profiles add column public_player_id uuid not null default gen_random_uuid() unique;
alter table uno.player_profiles add column username text;
alter table uno.player_profiles add constraint username_format check (username is null or username ~ '^[A-Za-z0-9_]{3,24}$');
create unique index player_profiles_username_unique on uno.player_profiles(lower(username)) where username is not null;

create table uno.friendships (
 id uuid primary key default gen_random_uuid(),
 requester_id uuid not null references uno.users(id) on delete cascade,
 recipient_id uuid not null references uno.users(id) on delete cascade,
 status text not null default 'pending' check(status in ('pending','accepted','declined','cancelled','removed')),
 created_at timestamptz not null default now(),
 responded_at timestamptz,
 check(requester_id <> recipient_id)
);
create unique index friendships_active_pair on uno.friendships(least(requester_id,recipient_id),greatest(requester_id,recipient_id)) where status in ('pending','accepted');
create index friendships_requester_idx on uno.friendships(requester_id,status);
create index friendships_recipient_idx on uno.friendships(recipient_id,status);

create table uno.player_blocks (
 blocker_id uuid not null references uno.users(id) on delete cascade,
 blocked_id uuid not null references uno.users(id) on delete cascade,
 created_at timestamptz not null default now(),
 primary key(blocker_id,blocked_id),
 check(blocker_id <> blocked_id)
);
create index player_blocks_blocked_idx on uno.player_blocks(blocked_id);

create table uno.room_invitations (
 id uuid primary key default gen_random_uuid(),
 room_id uuid not null references uno.rooms(id) on delete cascade,
 sender_id uuid not null references uno.users(id) on delete cascade,
 recipient_id uuid not null references uno.users(id) on delete cascade,
 status text not null default 'pending' check(status in ('pending','accepted','declined','cancelled','expired')),
 created_at timestamptz not null default now(),
 expires_at timestamptz not null default (now() + interval '5 minutes'),
 responded_at timestamptz,
 check(sender_id <> recipient_id),
 check(expires_at > created_at)
);
create index room_invitations_recipient_idx on uno.room_invitations(recipient_id,status,expires_at);
create unique index room_invitations_pending_unique on uno.room_invitations(room_id,sender_id,recipient_id) where status='pending';

alter table uno.friendships enable row level security;
alter table uno.player_blocks enable row level security;
alter table uno.room_invitations enable row level security;
revoke all on uno.friendships, uno.player_blocks, uno.room_invitations from public, anon, authenticated;
commit;
