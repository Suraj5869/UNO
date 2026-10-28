# Game Rules and Variant Plan

## Rule-source policy
Do not copy artwork or proprietary assets. Before public/commercial release, review intellectual-property and licensing requirements. Rules must be implemented from an agreed and appropriately sourced rules specification. Record any house-rule choices here.

## Initial target: Classic
Planned card/action categories:
- Number cards and colors.
- Skip.
- Reverse.
- Draw Two.
- Wild.
- Wild Draw Four.
- Draw pile, discard pile, active color, turn direction, current player.
- UNO call and missed-call handling according to the selected rules.
- Win detection and result recording.

## Decisions still required
- Whether a player who draws a playable card may play it immediately.
- Whether draw-card stacking is allowed as an optional house rule.
- Wild Draw Four challenge behavior and penalty resolution.
- Exact missed-UNO window and penalty.
- How the initial discard is resolved for each action-card type.
- What happens when the draw pile empties.
- Whether scoring continues across rounds or each match is one round.

Do not guess silently: settle these questions before implementing the relevant rule tests.

## Variant roadmap
1. Classic.
2. UNO Flip.
3. UNO Show 'em No Mercy.
4. Other variants only after requirements/licensing review.

Each variant needs its own deck definition, supported player range, rule tests, and clear state-transition rules. The shared engine must not assume all variants share the same deck or special actions.
