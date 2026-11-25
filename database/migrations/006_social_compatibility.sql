begin;
create unique index if not exists friendships_unordered_pair_unique on uno.friendships(least(requester_id,recipient_id),greatest(requester_id,recipient_id));
create table if not exists uno.live_room_invitations (
 id uuid primary key default gen_random_uuid(),
 sender_id uuid not null references uno.users(id) on delete cascade,
 recipient_id uuid not null references uno.users(id) on delete cascade,
 room_code text not null,
 created_at timestamptz not null default now(),
 expires_at timestamptz not null default now()+interval '5 minutes',
 check(sender_id<>recipient_id)
);
create index if not exists live_invite_recipient_idx on uno.live_room_invitations(recipient_id,expires_at);
alter table uno.live_room_invitations enable row level security;
revoke all on uno.live_room_invitations from public,anon,authenticated;
commit;
