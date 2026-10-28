# Project Overview

## Vision
Build an original, cross-platform multiplayer card-game platform inspired by the social experience of digital UNO games. The platform starts with a Classic UNO-style mode and is architected to support additional variants later. Product branding, artwork, and any commercial use of UNO trademarks/rules must be reviewed for intellectual-property and licensing requirements before public launch.

## Target platforms
- Web browser.
- Android.
- iOS.
- Cross-platform rooms: web and mobile users can join the same match.

## Core experiences
1. Play with bots.
2. Create a private room and invite others with a room code/link.
3. Join public matchmaking.
4. See basic profile and match results.
5. Reconnect after temporary network loss.
6. Add more game variants after the first variant is stable.

## Initial scope
- Server-authoritative Classic rules engine.
- 2–10 total participants in the shared engine/room design, subject to deck/rule validation.
- Humans and bots in a room, never exceeding the configured room capacity.
- Automated tests for card actions, turn progression, penalties, win detection, and edge cases.
- Web client first; mobile client follows using the same backend and API contracts.

## Not in the first MVP
- Real-money wagering or cash prizes.
- Purchases, complex currencies, or monetization.
- Voice chat.
- Spectator mode.
- Ranked tournaments.
- Multiple variants before Classic is tested.

## Success criteria for the first playable milestone
A player can start a complete game against bots, play valid cards, draw, use action/wild cards, call UNO under the selected rule set, and reach a correct winner state. The server rejects invalid/out-of-turn actions and does not leak hidden cards.
