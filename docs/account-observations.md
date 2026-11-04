# Backend account observations

The Firebase bearer validator invokes `AuthenticatedAccountObserver` **after** the
existing `IPremiumAccountStore.UpdateVerifiedIdentityAsync` operation. No frontend
release is required. Neither premium grants nor identity/authorization behavior is
changed. Anonymous/legacy UID requests and browser-only authentication are outside
coverage. This does not create Firebase accounts: it observes their real creation.

## Configuration and privacy

Set `Firebase:WebApiKey` (`Firebase__WebApiKey` in environment settings) to the Web
API key for the already configured Firebase project. `Firebase:AccountsOrigin` is
optional and defaults to `https://identitytoolkit.googleapis.com`; only that HTTPS
origin is accepted, with no credentials, non-default port, path, query or fragment.
The project/key must allow Firebase Authentication `accounts:lookup` for the
incoming ID token. This is a per-user lookup, not privileged directory enumeration.
No admin credentials, browser token extraction, or additional authorization grant
are required. A missing/restricted key leaves creation unknown, not registered.

The validated request's actual token is sent only to the allowlisted origin, in a
POST body. Redirects are disabled. HTTP-client logging is disabled for this named
client and an Application Insights initializer strips API keys from dependencies.
Tokens, keys, UID, email, names and arbitrary provider bodies are never logged or
persisted by this observer. Account pseudonyms reuse `UsageTelemetry` hashing.
`client_version` beginning with `metrics-verification` (case insensitive, trimmed)
is excluded from usage telemetry and registration/auth-observation persistence.

## Metrics contract

All observer events use `account_key` equal to the existing hashed `user_Id`,
`identity_source=authenticated`, `coverage=observed_authenticated_backend`,
and `auth_observation_version=1`. Existing backend/environment/client dimensions
remain available. Dates below are UTC ISO-8601, not telemetry ingestion time.

| Event | Stable deduplication key | Occurrence date and definition |
| --- | --- | --- |
| `AccountAuthenticatedObserved` | `observation_key` = hash(UID, UTC day) | `activity_date`: distinct authenticated accounts observed by this backend that UTC day; `observed_at` records the request observation. |
| `AccountSignInObserved` | `signin_key` = hash(UID, verified JWT `auth_time` seconds) | `authenticated_at`: actual auth time; `first_observed_at` is when the backend first observed it. Refresh/requests never create a new sign-in. |
| `AccountFirstSeenObserved` | `account_key` | `first_seen_at`: first backend observation since this tracking began; **not a registration**. |
| `AccountRegistrationObserved` | `registration_key` (existing hash of UID) | `created_at`: Firebase `accounts:lookup` creation milliseconds; `registration_source=firebase_accounts_lookup`, `registration_version=2`, `registration_method=unknown`, `first_reported_at` is backend first-seen. |

Only one nonnegative, nonfuture integer `auth_time` claim is accepted. `iat` is
never used for sign-in or creation. Missing/invalid auth time does not prevent
daily authenticated use or creation lookup. Missing/malformed/future creation
timestamps, unmatched `localId`, or failed lookup never become registrations.
At deployment, previously existing users populate their original creation/auth
dates retrospectively, not the deployment day. Sign-ins are **observed sign-ins**,
not all Firebase logins or guaranteed human sessions: events without subsequent
backend traffic cannot be seen; auth times have one-second resolution.

Workbooks must first deduplicate stable keys, then group by occurrence fields,
not `customEvents.timestamp`. Search a sufficiently long ingestion window to
capture retrospective observations; a daily timestamp filter alone is incorrect.
When combining the pre-existing client new-user registration endpoint with server
observations, prefer `firebase_accounts_lookup` for each `registration_key`.
Keep `firebase_client_new_user` explicitly classified as client assertions.
For verified registrations, filter to the server source. First-seen is never a
fallback registration source. These are observed accounts, not a directory census.

## Durability, load and failure behavior

The existing `Premium:TableName` (default `PremiumAccounts`) is reused, but analytics
has separate partitions: `AccountObservationMetadata`, `AccountObservationFact`,
and server-authoritative `RegistrationVerified`. Existing `RegistrationObserved`
client facts remain insert-only and cannot poison verified dates.
No changes are made to `VerifiedIdentity`, Account or Email grants.

An ETag reservation allows one lookup per account across instances, with a six-hour
retry cooldown. Successful creation is cached permanently in the table. Failures
including cancellation reserve the same cooldown, preventing request storms;
config fixes may consequently take up to six hours to enrich previously seen
accounts. Process memory is bounded to 10,000 entries with 64 fixed contention gates.
Daily/sign-in facts are cached until UTC midnight; metadata is cached for five
minutes. Eviction, restart or another instance can re-project an existing fact.
Always deduplicate keys; event count is not a reliable count of sign-ins/accounts.

The entire observation has a two-second cancellation budget, including storage and
gate waiting; provider HTTP has a one-second budget and 64-KiB response limit.
It honors request cancellation and fails open, with sanitized warning categories
and `AccountObservationFailed.failure_category`. Failures never replace identity
validation errors or change authentication outcome. There is no durable telemetry
delivery receipt/outbox: SDK or ingestion loss can undercount until another request
re-projects that fact. Telemetry availability is not guaranteed during an outage.
Durable observation rows currently have no automatic retention/deletion policy;
account/day and sign-in rows grow with observed traffic.

Focused regressions:

```powershell
dotnet run --project scripts\RatingRegression\RatingRegression.csproj -- --account-observations
dotnet run --project scripts\RatingRegression\RatingRegression.csproj -- --usage-metrics
```
