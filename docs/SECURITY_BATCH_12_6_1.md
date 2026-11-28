# Batch 12.6.1 — authentication security

Status: partial, unverified; do not merge or deploy.

- Browser account access token is memory-only; refresh uses the existing Secure HttpOnly cookie and X-Uno-Browser header. Web Locks serialize cookie operations where supported.
- Login counters partition by identifier and source IP. Trusted proxy configuration and distributed abuse controls remain required.
- Logout and detected refresh reuse revoke room seats account-wide in the current process. This is not individual session binding or durable revocation. Other sessions of the same account lose room seats.
- Rematch cleanup removes stale seat metadata.

Remaining: bind room seats to validated session IDs on HTTP and SignalR; enforce expiration and rotation; reconcile revocation on restart and database-side expiry; verify cookie site topology and cross-tab fallback; implement email ownership verification with a configured delivery provider; run all regression tests. Existing browser-host mocks may require cookie support.

Release checks: verify refresh cookie flags, no tokens in browser storage, reload and concurrent refresh, logout and reuse revocation, guest reconnect, account isolation, expired sessions, rematches, login throttling across IPs. Run dotnet test src/backend/Uno.sln and npm ci, npm run build, npm run test:e2e in src/web. No test execution claimed.

## Session binding update

HTTP room operations and hub methods now check the seat's account and session ID. Logout and refresh reuse revoke only the affected session. SignalR accepts JWT on its hub path and closes on authentication expiry. Three RoomStore regression tests were added, not executed. Session bindings are currently process-local and not restored from checkpoints; restored account seats fail closed on checked operations. Social invite binding, database-side revocation of existing connections, durable binding, room credential expiry/rotation, email verification and end-to-end tests remain outstanding. Do not deploy.
