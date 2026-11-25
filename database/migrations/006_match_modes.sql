begin;
alter table uno.matches add column if not exists match_mode text not null default 'rooms' check (match_mode in ('rooms','bots','ranked'));
alter table uno.matches add column if not exists bot_difficulty integer check (bot_difficulty between 0 and 2);
alter table uno.match_players add column if not exists abandoned boolean not null default false;
commit;
