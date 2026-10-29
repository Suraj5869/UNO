# Classic UNO Rules and Variant Plan

## Rule-source policy

Implement the agreed rule specification, and do not copy proprietary artwork or assets. Review intellectual-property, branding, and licensing requirements before public or commercial release. House rules must be explicit room settings rather than hidden engine behavior.

## Classic deck and setup

- Use the standard 108-card Classic UNO deck.
- Support 2–10 total participants (humans plus bots).
- Deal seven cards to each player.
- The current setup implementation starts with a number card as the first discard. This is an intentional temporary simplification until we implement and test the official initial-action-card rules.

## Official Rules baseline

- A normal card is playable when its color matches the active color, its number matches the top number card, or its action matches the top action card.
- A Wild card can be played and the player chooses the active color.
- A Wild Draw Four may be challenged. The engine records whether the player held a card matching the previously active color when the card was played.
- If a player draws a playable card, they may play that drawn card immediately or pass; they may not play a different card from their hand after drawing.
- Skip skips the next player.
- Reverse changes direction. In a two-player game, Reverse acts like Skip, so the player who played it takes another turn.
- Draw Two makes the next player draw two cards and lose their turn.
- Draw-card stacking is **not allowed** in Official Rules.
- When the draw pile is empty, reshuffle the discard pile except for the top discard to make a new draw pile.

## House Rules configuration

Private-room creators can choose Official Rules or House Rules before the game starts. The selection is locked once play begins.

- House Rules may enable draw stacking.
- Stacking restrictions (same-type only versus mixed Draw Two/Wild Draw Four) must be selected explicitly before implementing the feature. Do not assume one stacking interpretation silently.
- Each room's rule configuration must be stored with the game state and enforced by the server.

## Still to implement or decide

- UNO call, missed-call timing, and penalty.
- Exact initial discard behavior for Skip, Reverse, Draw Two, Wild, and Wild Draw Four.
- Exact permitted stacking combinations for House Rules.
- Match scoring and whether a match consists of multiple rounds.
- Behavior when no cards remain anywhere to draw after recycling.

## Variant roadmap

1. Classic.
2. UNO Flip.
3. UNO Show 'em No Mercy.
4. Other variants after requirements and licensing review.

Each variant needs its own deck definition, supported player range, rule tests, and clear state-transition rules. Shared infrastructure must not assume all variants use the same deck or special actions.
