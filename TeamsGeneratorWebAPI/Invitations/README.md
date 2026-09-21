# Match occurrence invitations and Android notification digests

## Current rollout scope

Phone notifications are deferred. The invitation page, In/Out responses, waiting list,
and organizer attendance refresh remain active without Firebase messaging setup.
The frontend defaults `REACT_APP_ENABLE_INVITATION_PUSH` to off: notification settings,
native registration, and push-related authentication cleanup do not run.
The backend does not start the digest worker unless `Notifications:Fcm:Enabled=true`,
so the RSVP-only rollout does not require Always On for a notification timer.

The notification implementation below is retained for a later feature. Enabling it
requires `REACT_APP_ENABLE_INVITATION_PUSH=true` at frontend/Android build time,
server FCM opt-in, provisioning, and a coordinated rollout. Do not enable these
switches as part of the RSVP-only release.

## API

Match invitations are an account-wide Premium opt-in. `config.enableMatchInvitations`
defaults to `false` for new and existing accounts without the field, and is saved
through `POST /Config/Upload?scope=global`. Enabling requires an authenticated
Premium account; free accounts can turn it off. Group-only saves cannot change
the flag. `GET /Home` exposes the requester's `entitlements.canUseMatchInvitations`
and a separate `invitationAccess: { enabled: boolean }` for the selected group.
The latter combines the **canonical owner's** live entitlement and global flag;
it never replaces the requester's own global config or entitlement in shared groups.

Older clients that omit `enableMatchInvitations` on global or combined settings
saves preserve the stored preference. An explicit `false` disables it; a missing
field on an account with no stored opt-in remains `false`. There is no account
migration or bulk reset of previously enabled preferences.

Email-based Premium grants work for UID-only owner policy checks too. After Firebase
JWT validation, the backend remembers the UID's verified email row key in the existing
Premium table's separate `VerifiedIdentity` partition. Only authenticated, verified
claims establish this binding; request bodies and query parameters cannot set it.
An authenticated email change replaces it, and missing/unverified email clears it.
Existing email-only owners establish the binding on their next authenticated request.
UID-only reads (including public RSVP and shared groups) always reread the live `Email`
grant: disabling, deleting or expiring it takes effect without copying a permanent
`Account` grant. Identity bindings are not entitlement grants.

Creation and every anonymous response recheck the canonical owner's current
entitlement and global setting. A Premium co-organizer cannot bypass a disabled
or free owner; a free co-organizer can share an enabled Premium owner's group.
Participants do not need accounts or Premium. Disabling invitations or losing
Premium makes existing links read-only with `status: "closed"` while retaining
the original occurrence, responses, FIFO order, revision and journal. Re-enabling
reopens an unrevoked future occurrence with the same token and response history;
past occurrences remain closed. No roster, match history or queue document is
rewritten by these policy changes. Disabled creation returns 403; disabled RSVP
returns 409, including requests that would otherwise be noops.

Organizer and notification endpoints require the existing Firebase bearer JWT.
Identity comes only from its verified user claim; no `uid`/`ownerId` fallback is accepted.
Group references resolve through `IGroupCollaborationService.ResolveAccessAsync`,
then group existence and the live roster are checked using existing storage connectors.

| Endpoint | Request | Response |
| --- | --- | --- |
| `POST /Invitations` | `{groupId, scheduledAt, timeZone}` | `OrganizerInvitation` directly |
| `GET /Invitations/Current?groupId=...&scheduledAt=...` | UTC ISO occurrence timestamp | `{invitation: OrganizerInvitation|null}` |
| `GET /Invitations/{id}` | Authenticated notification recipient; exact occurrence ID | `OrganizerInvitation` directly |
| `GET /Invitations/Public/{token}` | Anonymous capability | `PublicInvitation` directly |
| `POST /Invitations/Public/{token}/Responses` | `{playerKey, status: "in"|"out"}` | `PublicInvitation` directly |
| `DELETE /Invitations/Current?groupId=...&scheduledAt=...` | Authenticated revocation of that occurrence | `{success:true}` |
| `GET /Notifications/Preferences` | Authenticated | `{enabled, playerThreshold, intervalMinutes, deliveryConfigured}` |
| `PUT /Notifications/Preferences` | `{enabled, playerThreshold, intervalMinutes}`; threshold 1..50, interval 60..1440 | Saved three-field preferences directly |
| `POST /Notifications/Devices` | `{token, platform:"android"}` | `{success:true}` |
| `DELETE /Notifications/Devices` | `{token}` (body, authenticated before logout) | `{success:true}` |

`PublicInvitation` has exactly `id`, `groupName`, `matchName`, `location`,
`groupLogoDataUrl` (nullable), `scheduledAt` (UTC ISO), `timeZone` (IANA), `language`,
`capacity`, `status` (`open`/`closed`), `revision`, `players`, and `counts`.
Each player contains only `{key,name,status,waitingListPosition}`.
Status is `unanswered`, `in`, or `out`. Positive waiting position means waiting;
an `in` with null waiting position is selected. Counts are `{in,waiting,out}`.
`OrganizerInvitation` adds `token` and canonical `groupId`.
Both DTOs load `language` from the live canonical owner's global preference:
trimmed, case-insensitive `he`, `heb`, `he-il` and `iw` map to `he`; all other
values map to `en`. Language is not frozen with the occurrence or taken from
the anonymous visitor, co-organizer or group-scoped configuration.

`deliveryConfigured` is server-owned, read-only configuration readiness: FCM opt-in,
project ID and successfully resolved ADC/Firebase initialization are all required.
Credential discovery is bounded to five seconds and negative results are cached for
five minutes. It does not depend on browser/Android permission, device registration,
or the user's `enabled` preference. Settings must show that phone delivery is not
provisioned when false. A true value is not a delivery receipt or a guarantee that
network, Google IAM/FCM API permissions, or an individual device will accept a send.
PUT cannot set or persist this field; its writable contract remains three fields.

Notification opens use `GET /Invitations/{id}`, not Current or recalculated recurrence
dates. It verifies both the canonical group access/existence and recipient scope:
only the canonical owner (the current digest recipient) can fetch by ID; membership
alone does not grant this lookup. Unknown/nonrecipient IDs return 404, denied group
access returns 403, and revoked occurrences return 410. Past occurrences still return
the original closed detail, including after public-capability expiry. This read never
changes configured schedule, creates a later occurrence, or grants writes to a closed one.

The group owner, canonical group and exact UTC instant determine a unique immutable
occurrence ID. Resharing returns the same random 256-bit capability. A future weekly
occurrence gets a separate ID/token. Name/logo/location/capacity/timezone are frozen
at creation; actual player keys/names are loaded from the live roster. Deleted players
are filtered, unknown/stale IDs are rejected, and duplicate names remain distinct.
No shared-link sign-in or cancellation claim is required: this is explicitly a
trusted private group, and anyone possessing the link can change any listed response.
Never distribute the link outside that group.

Revision starts at 0. Initial Out leaves a player unanswered and performs no write.
Identical responses do nothing. In, withdrawal after In, and rejoining increment
revision and append an event in the same conditional blob write. Response ordering
selects capacity; withdrawals immediately promote waiting players. Rejoining goes
to the back. These records never overwrite global `isArrived`, saved teams, match
history, ratings, private health data, or chemistry/reset state.

At kickoff the link becomes read-only/closed and still shows the **original**
occurrence, never a calculated next date. The public capability expires 30 days
after kickoff (410); organizer Current can still retrieve history. Revocation
invalidates the public token immediately (404) and prevents resharing that occurrence.
Validation uses 400; missing capability/group 404; denied group 403; expired/revoked
creation 410; closed write/contention 409; rate limit 429; unavailable roster/config 503.
Errors have `{error: string}` (framework model-validation errors use ProblemDetails).

## Frontend integration and release order

The frontend gates `InvitationsContext` on `Home.invitationAccess.enabled`, not the
requester's own global setting or entitlement. Missing metadata fails closed.
Disabled accounts keep manual attendance/generation without calling invitation APIs.
Settings saves reload Home for authoritative effective owner policy; stale account,
group and background-poll responses cannot overwrite that refresh.

When enabled, `InvitationsContext` overlays attendance for the active occurrence in
`PlayersContext`; it does not write the base roster's `isArrived`. Generation first
refreshes `GET /Invitations/Current` for that exact group/UTC occurrence, then derives
selected and waiting players from the snapshot. Organizer In/Out actions POST the same
capability response endpoint, so polling observes the persisted override instead of
overwriting a local-only change. All Out submits Out for every current `in` player,
including waiting players. Base roster skills/private fields, saved history and prior
reset behavior remain unchanged.

The public page uses an explicit single-line player selector (no automatic first
player), In/Out actions, and initially collapsed rosters. Stable player IDs are
remembered per occurrence, including duplicate names. `?lang` is only a loading/error
hint; the live DTO language is authoritative. Hebrew uses RTL and Israeli date/24-hour
formatting in the event's timezone. Organizer entry buttons stay English; their
full-size dialog and share message follow the organizer's configured language.

Shared links preserve their original slot. After a past weekly occurrence, preparing
the next occurrence requires the explicit **Prepare Next** action; opening a share
link or notification must never silently advance the configured schedule.

For deployed builds, set `REACT_APP_PUBLIC_APP_URL` to the **HTTPS public SPA
origin**, not the backend API URL, localhost or a native-webview origin. Its known
deployed default is:

```text
https://kind-smoke-08fad490f.6.azurestaticapps.net
```

Public share links use `${REACT_APP_PUBLIC_APP_URL}/invite/{token}`. Configure the SPA
host to serve `index.html` for direct navigation/refresh of `/invite/{token}` (Azure
Static Web Apps navigation fallback or equivalent). This route must work without
organizer sign-in; preserve real asset/API routes and do not turn API 404s into SPA
HTML. Configure the API's allowed CORS origins for the chosen public frontend origin.
Redact `/invite/*` capability paths in frontend hosting/analytics logs as well as the
backend's `/Invitations/Public/*` paths.

For local-only testing, restart the updated API on port 5236 and set the frontend's
ignored `.env.development.local` before restarting its development server:

```dotenv
REACT_APP_USE_LOCAL_API=true
REACT_APP_LOCAL_API_URL=http://localhost:5236
REACT_APP_PUBLIC_APP_URL=http://localhost:3000
```

HTTP invitation links are accepted only in development with the local API enabled,
and only for loopback hosts (`localhost`, `127.0.0.1`, `[::1]`). The share dialog labels
these as local test links; they cannot be shared with players on other devices.
Production and native builds retain HTTPS-only links. Running the API locally does
not isolate its configured cloud storage. An older API's missing invitation routes
must not be mistaken for an invitation that was deleted.

**Release the backend BEFORE the frontend.** For enabled groups the frontend calls
Current during team generation even before a user creates an invitation; the backend must already
support Current, Create, public RSVP, exact-ID notification lookup and preferences
with `deliveryConfigured`. Releasing the frontend first can break generation against
an older API. Confirm a healthy backend and authenticated Current returning either
the expected snapshot or `{invitation:null}` before promoting the frontend.
No release or production validation was performed as part of this implementation.

## Storage and aggregation

The existing private players blob container holds `match-invitations/v1/`:
`state/` uses hashed canonical occurrence IDs; `capabilities/` uses SHA-256 of the
random capability, never the token in a blob name; `accounts/` uses hashed Firebase UID.
`device-owners/` holds a global ETag-protected owner record keyed by hashed FCM token.
Each occurrence is one ETag-conditional JSON document containing response state,
event journal, delivered baseline, pending timestamp, outbox and lease. This is the
transaction boundary: no RSVP can commit without its corresponding event/queue state.
Concurrent CAS failures retry and ultimately return 409 rather than lose a response.
Capability-index creation is repairable on re-share after an interrupted create.

Digests target the **canonical group owner's** privately registered Android devices
and preferences. They do not target anonymous players or broadcast device tokens.
Defaults are enabled, **10 DISTINCT net-changed players OR 360 minutes**.
The deadline starts at the first pending unsent event, not the last change.
Multiple changes by one player coalesce to one latest meaningful status against the
last successfully delivered baseline. A real **In -> Out remains a withdrawal even
when the preceding In was still unsent**; it must not disappear just because the
initial baseline was unanswered. Initial Out and identical noops do not queue anything.
The acknowledged revision prevents an already delivered withdrawal from replaying.
The worker aggregates each occurrence separately with a summary such
as `3 joined, 1 withdrew; 15/15 places filled`. No urgent exemption exists.
Normal batches have a 10-minute successful-delivery cooldown.

`InvitationDigestWorker` polls one page of up to 64 state documents every 30 seconds,
advancing a continuation cursor; it never flushes just because an app opens.
All instances contend for a persisted two-minute renewable ETag lease. Delivery
snapshots and per-device receipts survive restart; requests continue committing
responses while delivery is in progress. Failed sends retry exponentially (60 seconds
to one hour), preserve their baseline, and recompute unsent net status before retry.
Disabled preferences intentionally drain to the current baseline without sending.
Unconfigured/failed delivery and accounts without usable devices do **not** advance it.
No late digests are initiated after kickoff/revocation.

FCM has no transactional exactly-once send operation. If the process crashes after FCM
accepted a send but before the receipt write, one retry can duplicate that digest.
Stable `digestId`, Android collapse key and notification tag mitigate duplicates.
Completed-device receipts prevent resend to successful devices when another fails.
This is at-least-once delivery, not a misleading exactly-once guarantee.

Device registration refreshes its private 90-day validity (maximum 20 devices/user).
Unregistered/wrong-project FCM tokens and expired tokens encountered for a due digest
are removed, conditional on the registration not having been refreshed concurrently.
The client must DELETE before signing out or switching accounts.
Registration is externally idempotent and a token has only one authoritative owner
across accounts. Registering it for a different authenticated account transfers
ownership and removes the previous inventory entry. The worker checks the global
owner immediately before every send; a stale account inventory is never authority.
Versioned ownership protects refreshed registrations from stale invalid-token cleanup.
An interrupted POST can be safely retried, and DELETE also succeeds after removal or
if a different account now owns the token (without touching that other account).
Tokens persisted before the ownership registry existed must be re-registered by the
client before sending; the server does not guess ownership from legacy inventories.
As with any push provider, a message already accepted/in flight cannot be retracted.
FCM registration tokens cannot be decoded/verified as Firebase JWTs: the endpoint
authenticates the owner with JWT; FCM validates registration tokens on delivery.

## Provisioning (not performed by this change)

1. Keep the existing private blob container/connection configuration and read/write
   permissions. Do not create a public container or expose blob credentials.
2. Enable Firebase Cloud Messaging HTTP v1 in the Firebase project and provision
   least-privilege permission to send FCM messages.
3. Set `Notifications__Fcm__Enabled=true` and `Notifications__Fcm__ProjectId` (or the
   existing `Firebase__ProjectId`) in the server's secure environment configuration.
   The default is OFF. Use Application Default Credentials (prefer workload identity
   federation). If a service account file is unavoidable, securely mount it outside
   the repository and configure `GOOGLE_APPLICATION_CREDENTIALS` to its path.
   Never put credential contents, device tokens, or capabilities in appsettings,
   source control, build output, telemetry, or support logs.
4. Native Android must use the same Firebase project, register its current FCM token,
   request Android notification permission independently from server preferences,
   and handle foreground/notification-open events. Notification+data FCM messages
   display with the app closed. Data contains `invitationId`, `groupId`, `scheduledAt`,
   and stable `digestId`; dispatch `teamify:notification-open` to the web application.
   Delivery TTL is bounded by six hours and kickoff; normal priority and the native
   `matchday-rsvp` Android notification channel are used.
   Provision `android/app/google-services.json` for application ID `teams.generator.new`;
   the file is currently missing. Keep service-account credentials out of the Android
   application. Its notification channel is `matchday-rsvp`.
5. **Azure App Service Always On is required** for this in-process BackgroundService.
   Autosleep suspends polling/deadlines. All instances must share the same storage.
   At higher volumes move the processor behind a continuously running queue worker
   or indexed due-work scheduler; bounded scans trade throughput for simplicity and
   actual delivery can lag the deadline by scan time.
6. Keep proxy/App Service/IIS access logging from recording `/Invitations/Public/*`
   capability URLs; disable URL/body capture for these routes at the ingress.
   Application Insights request URLs/operation names are redacted, and default
   ASP.NET hosting request-start/end logs are suppressed in code. Do not enable
   HTTP request-body logging (device tokens). Public responses set no-store,
   no-referrer and noindex headers.
7. Public throttling is bounded in-memory, per API instance: 600 calls/minute per
   token+address and 30 writes/minute per token+player. This is generous for household
   NATs and does not use untrusted forwarded IP headers. Put a distributed edge
   limiter/WAF in front for horizontally scaled or internet-scale abuse protection.

Without FCM opt-in/credentials RSVP still works. The server emits an explicit
configuration/credential warning, retains pending notifications, and never pretends
to have delivered a push. No credential files or cloud data were accessed during
implementation or regression execution.

**Actual phone push delivery remains a provisioning blocker.** Neither this code,
Android permission approval nor device registration provisions FCM/ADC, IAM/API
permissions or App Service Always On. Until those are supplied, Settings must display
the unprovisioned state from `deliveryConfigured:false`; do not describe push as live.
After provisioning, a separately authorized real-device end-to-end delivery check is
still required to establish actual notification delivery.

## Local regression runner

The existing `scripts\RatingRegression` executable includes in-memory invitation,
roster, clock, document store and sender providers; no cloud network or new test
framework is involved. It covers frozen/isolated identity, private-field projection,
waiting order/promotion, deleted/duplicate-name players, initial Out/noops, CAS races,
auth attributes/claims, preference validation, deadline/threshold/cooldown,
coalescing (including In-to-Out before the first digest), durable retries/leases,
cleanup, revocation and expiry. Owner policy assertions also cover missing-field
defaults, account/group save isolation, Premium denial, shared-owner authorization,
Home availability without requester-global leakage, live language changes,
disable/re-enable preservation and live entitlement downgrade without cloud access.

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet build scripts\RatingRegression\RatingRegression.csproj --configuration Release -p:OutputPath="$PWD\scripts\RatingRegression\artifacts\invitations\"
dotnet scripts\RatingRegression\artifacts\invitations\RatingRegression.dll
```

The isolated output avoids locking the running Debug server DLLs.
