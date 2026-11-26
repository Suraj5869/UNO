begin;
alter table uno.cosmetic_items add column if not exists required_level integer not null default 1 check(required_level>=1);
alter table uno.cosmetic_items add column if not exists required_season_xp integer not null default 0 check(required_season_xp>=0);
insert into uno.cosmetic_items(item_key,category,display_name,required_level,required_season_xp) values
('avatar_gold','avatar','Golden Ace',2,0),
('avatar_neon','avatar','Neon Champion',5,0),
('card_back_crimson','card_back','Crimson',3,0),
('card_back_royal','card_back','Royal Purple',6,0),
('table_theme_ocean','table_theme','Ocean Arena',4,0),
('table_theme_midnight','table_theme','Midnight Arena',8,0),
('avatar_season','avatar','Season Star',1,500),
('card_back_season','card_back','Season Aurora',1,1000),
('table_theme_season','table_theme','Season Emerald',1,1500)
on conflict(item_key) do nothing;
insert into uno.player_inventory(user_id,item_id)
select p.user_id,c.id from uno.player_progress p cross join uno.cosmetic_items c
where c.is_active and c.required_season_xp=0 and p.level>=c.required_level
on conflict do nothing;
insert into uno.player_loadouts(user_id,category,item_id)
select p.user_id,c.category,c.id from uno.player_progress p cross join uno.cosmetic_items c
where c.item_key in ('avatar_default','card_back_classic','table_theme_default')
on conflict do nothing;
commit;
