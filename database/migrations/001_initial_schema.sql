begin;
create schema if not exists uno;
revoke all on schema uno from public, anon, authenticated;

create table uno.users (
 id uuid primary key default gen_random_uuid(),
 email text not null check (length(trim(email)) > 0),
 password_hash text not null,
 created_at timestamptz not null default now(),
 updated_at timestamptz not null default now()
);
create unique index users_email_unique on uno.users (lower(trim(email)));
create table uno.refresh_tokens (
 id uuid primary key default gen_random_uuid(),
 user_id uuid not null references uno.users(id) on delete cascade,
 token_hash text not null unique,
 expires_at timestamptz not null,
 revoked_at timestamptz,
 created_at timestamptz not null default now()
);
create index refresh_tokens_user_idx on uno.refresh_tokens(user_id);
create table uno.player_profiles (
 user_id uuid primary key references uno.users(id) on delete cascade,
 display_name text not null check (length(trim(display_name)) between 1 and 24),
 avatar_key text not null default 'default',
 created_at timestamptz not null default now()
);
create table uno.player_progress (
 user_id uuid primary key references uno.users(id) on delete cascade,
 level integer not null default 1 check(level >= 1),
 total_xp bigint not null default 0 check(total_xp >= 0)
);
create table uno.player_statistics (
 user_id uuid primary key references uno.users(id) on delete cascade,
 matches_played integer not null default 0 check(matches_played >= 0),
 wins integer not null default 0 check(wins between 0 and matches_played)
);
create table uno.cosmetic_items (
 id uuid primary key default gen_random_uuid(),
 item_key text not null unique,
 category text not null check(category in ('avatar','card_back','table_theme')),
 display_name text not null,
 is_active boolean not null default true,
 unique(id,category)
);
create table uno.player_inventory (
 user_id uuid not null references uno.users(id) on delete cascade,
 item_id uuid not null references uno.cosmetic_items(id),
 unlocked_at timestamptz not null default now(),
 primary key(user_id,item_id)
);
create table uno.player_loadouts (
 user_id uuid not null,
 category text not null,
 item_id uuid not null,
 primary key(user_id,category),
 foreign key(user_id,item_id) references uno.player_inventory(user_id,item_id) on delete cascade,
 foreign key(item_id,category) references uno.cosmetic_items(id,category)
);
create table uno.rooms (
 id uuid primary key,
 code text not null check(length(code)=6),
 host_user_id uuid references uno.users(id),
 visibility text not null default 'private' check(visibility in ('private','public')),
 variant text not null default 'classic',
 rule_set integer not null check(rule_set in (0,1)),
 maximum_players integer not null check(maximum_players between 2 and 10),
 status text not null check(status in ('waiting','in_progress','completed','cancelled')),
 created_at timestamptz not null default now(),
 closed_at timestamptz
);
create unique index rooms_active_code_idx on uno.rooms(code) where closed_at is null;
create table uno.room_members (
 id uuid primary key,
 room_id uuid not null references uno.rooms(id),
 user_id uuid references uno.users(id),
 display_name text not null,
 access_token_hash text,
 joined_at timestamptz not null default now(),
 left_at timestamptz
);
create index room_members_room_idx on uno.room_members(room_id);
create unique index room_members_active_user_idx on uno.room_members(room_id,user_id) where left_at is null and user_id is not null;
create table uno.matches (
 id uuid primary key,
 room_id uuid not null references uno.rooms(id),
 variant text not null,
 rule_set integer not null check(rule_set in (0,1)),
 status text not null check(status in ('in_progress','completed','cancelled')),
 started_at timestamptz not null default now(),
 completed_at timestamptz
);
create index matches_room_idx on uno.matches(room_id);
create table uno.match_players (
 match_id uuid not null references uno.matches(id),
 player_id uuid not null,
 user_id uuid references uno.users(id),
 display_name text not null,
 finish_position integer check(finish_position between 1 and 10),
 primary key(match_id,player_id)
);
create index match_players_user_idx on uno.match_players(user_id);
create table uno.match_snapshots (
 match_id uuid not null references uno.matches(id),
 revision bigint not null check(revision >= 0),
 state jsonb not null,
 created_at timestamptz not null default now(),
 primary key(match_id,revision)
);
create table uno.xp_transactions (
 id uuid primary key default gen_random_uuid(),
 user_id uuid not null references uno.users(id),
 match_id uuid references uno.matches(id),
 reason text not null,
 amount integer not null check(amount > 0),
 idempotency_key text not null unique,
 created_at timestamptz not null default now()
);
create index xp_transactions_user_idx on uno.xp_transactions(user_id);
create table uno.matchmaking_tickets (
 id uuid primary key default gen_random_uuid(),
 user_id uuid not null references uno.users(id),
 variant text not null default 'classic',
 rule_set integer not null default 0 check(rule_set in (0,1)),
 status text not null check(status in ('waiting','matched','cancelled','expired')),
 room_id uuid references uno.rooms(id),
 created_at timestamptz not null default now(),
 expires_at timestamptz not null
);
create unique index matchmaking_waiting_user_idx on uno.matchmaking_tickets(user_id) where status='waiting';
create index matchmaking_queue_idx on uno.matchmaking_tickets(variant,rule_set,created_at) where status='waiting';

-- Backend-only schema. No direct browser access, including hidden snapshots.
revoke all on all tables in schema uno from public, anon, authenticated;
do $$
declare t record;
begin
 for t in select tablename from pg_tables where schemaname='uno' loop
  execute format('alter table uno.%I enable row level security',t.tablename);
 end loop;
end $$;
commit;
