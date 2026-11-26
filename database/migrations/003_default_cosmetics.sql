begin;
insert into uno.cosmetic_items(item_key,category,display_name) values
 ('avatar_default','avatar','Default Avatar'),
 ('card_back_classic','card_back','Classic Card Back'),
 ('table_theme_default','table_theme','Default Table')
on conflict(item_key) do nothing;
commit;
