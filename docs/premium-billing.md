# Google Play Premium billing

Implemented and **off by default in source**. As of 2026-09-26, the isolated Azure sandbox has billing, recovery and checkout enabled. Signed internal build 216 has exercised Google license-test purchases and the declined-payment retry fix. The approved production rollout completed at 18:16:38 UTC: deployment `6e42048f57284c498170fb298f8d8277` runs .NET 10.0.11 with billing, recovery, checkout and worker-file logging disabled. Always On remains off. Original configuration/static files and storage/auth/manual-Premium bindings were preserved; authenticated customer/manual-Premium scenarios were not re-exercised. See the [rollout record](premium-production-rollout.md#scope-and-current-boundary) for exact package/serving identity, completed gates and remaining launch boundaries.

See [production preparation and rollout](premium-production-rollout.md) and the nonsecret [disabled production settings example](billing-production.settings.example.json). Preparing these source files does not deploy anything or authorize sales.

## Azure sandbox

The approved light-test environment uses a separate web app on the existing production S1 plan, not a deployment slot. CPU and memory are shared; there is no second compute-plan charge, but storage/network usage can incur charges. Do not load-test this shared plan.

- Resource group: `teamify-billing-sandbox-rg` (East US).
- Web app: `teamify-billing-sandbox-3e0e56`.
- API origin: `https://teamify-billing-sandbox-3e0e56.azurewebsites.net`.
- Storage: `teamifybillingsbx3e0e56`, new Standard LRS account with private containers. No production data, manual grants, storage credentials, AI credentials or notification credentials were copied.
- `Billing__Environment=SANDBOX`; `Enabled`, `RecoveryEnabled` and `CheckoutEnabled` are true for the approved internal purchase exercise. FCM delivery is disabled.
- Sandbox tables: `PremiumAccountsSandbox` and `PremiumSubscriptionsSandbox`; the latter is created only when billing is enabled and used.
- Firebase token validation still uses `teamsgeneratorapp`; this shares sign-in identities, not production roster or entitlement data. CORS allows the native `https://localhost` origin and this sandbox origin only.
- Always On is enabled for the sandbox receipt worker. That sandbox operation did not change production; production Always On remains off.
- On 2026-09-26, the separately approved sandbox rehearsal deployed `net10.0` with Windows App Service runtime `v10.0`, retaining its 32-bit in-process IIS worker. Live worker modules confirmed the installed .NET 10.0.11 runtime; the application-local JwtBearer/Newtonsoft packages are 10.0.12. The sandbox's previous `DOTNET_ROLL_FORWARD=Major` override was removed; the artifact uses the normal 10.0.0 minimum and patch selection. That rehearsal did not migrate production; the later separate billing-disabled production rollout is recorded above.

The initial sandbox deployment `d292316cf88643a68a49bc090859c15f` established isolated health/setup and authenticated billing routes. Later sandbox work observed authenticated Pub/Sub delivery (`ack_200`), monthly/yearly Google license-test purchases, and successful server-side RevenueCat receipt import. This is sandbox evidence, not production interoperability.

The .NET 10 rehearsal deployment is `8832321087b64e01b64514ddedbebae1` (ZIP SHA-256 `f3ac1bd395b1afce6fb33f5f1651380c071be7e40c6e7e9d41fa261224da6727`). Its configuration was packaged from the existing nonsecret sandbox settings, not source production configuration. The deployed API/library/package hashes matched the fresh publication; existing appsettings bytes and all Azure application settings except the approved roll-forward removal were preserved. Billing flags, isolated storage/table bindings, provider authentication/audience, the protected Google credential path, Always On, FCM-off and existing logging settings were unchanged. Private rollback artifact/configuration backups were retained.

Live HTTPS checks returned 200 for health and legacy setup, and 401 for unauthenticated billing status and both notification receivers, without redirects. The notification responses establish that ASP.NET reached the existing HTTPS/authentication checks; they do not prove authenticated provider delivery or client-IP attribution. No purchases, provider changes, explicit table-data writes or new phone scenarios were performed. Authenticated Firebase/manual Premium flows and Google `testNotification` were not re-exercised. The runtime rehearsal itself did not establish worker liveness; the separately authorized monitoring activation below adds that evidence without configuring App Insights.

The dedicated `teamify-billing-sandbox` Google service-account credential is provisioned outside public web content at `C:\home\data\teamify-billing\google-service-account.json`; only the sandbox's `GOOGLE_APPLICATION_CREDENTIALS` setting references it. It is not in the release ZIP, repository, frontend or production settings. Google purchase/order verification has been exercised with license-test purchases. Retain and rotate this private file separately from web deployments.

The RevenueCat Google Play public import key and App ID `app6d9e6c4b27` are configured in sandbox Azure settings. Its webhook authorization is held only in approved Azure/RevenueCat settings; the configured webhook test returned HTTP 200. Production requires its own reviewed configuration and delivery evidence.

The user created `teamify-billing-sandbox-push` on the existing `Play-Store-Notifications` topic. The receiver expects audience `https://teamify-billing-sandbox-3e0e56.azurewebsites.net/api/billing/google/notifications` and the dedicated `teamify-billing-sandbox` service-account email. Actual authenticated subscription delivery has been observed. Preserve RevenueCat's existing subscription. Never direct a production app to this sandbox origin.

## Architecture and ownership

Google Play product and RevenueCat entitlement are both **`teamify_premium`**. Auto-renewing base plans `monthly` / `yearly` have the same features and no trial; the approved US prices are USD 4.00 / 29.99. RevenueCat offering `default` should contain the imported Play monthly/annual packages, not TestStore products.

The prior RevenueCat-only architecture could assign an unknown receipt to whichever Teamify account was signed in at first native recovery. The implemented alternative uses supported Google Play BillingClient + Google Developer APIs, and RevenueCat's supported `POST /receipts` API as a server-side mirror. **Google-verified entitlement/ownership, not RevenueCat aliases/customer flags, is now authoritative.** RevenueCat remains the subscription dashboard and receives verified receipts; it is not silently removed, but its native checkout and entitlement authority are deliberately replaced. This tradeoff adds backend Google API credentials, direct acknowledgment responsibility and authenticated RTDN fan-out. Accept/configure these requirements before production sales; production settings remain disabled.

- `POST /api/billing/binding`, `POST /api/billing/sync`, `GET /api/billing/status` require verified Firebase identity and reject anonymous accounts.
- A random 256-bit opaque `tfp_`/`tfs_` App User ID is durably bound to the verified Firebase UID. Account and reverse mapping are one Azure Table transaction. Its uppercase SHA-256 digest is returned as `playAccountId` and explicitly passed to Play's `setObfuscatedAccountId` at purchase launch. No dependency on RevenueCat's private hash format.
- `POST /api/billing/google/recover` accepts **only a purchase token**, never a claimed UID/email/owner or entitlement. It durably stores the token, verifies it with Google's package-scoped `purchases.subscriptionsv2.get`, and resolves the immutable initiating account from Google's `externalAccountIdentifiers.obfuscatedExternalAccountId`.
- `POST /api/billing/google/resolution` accepts only a token hash and confirms definitive terminal resolution for the authenticated original owner. This can release a previously pending checkout journal after cancellation; an empty store query or inactive account alone cannot release it.
- Missing/unknown identifiers are not assigned to the current caller. For out-of-app resubscribe, Google's `outOfAppPurchaseContext.expiredExternalAccountIdentifiers` can establish the original identity. Before acknowledgment, the verified account/profile identifiers and resubscription provenance are persisted on that exact receipt. Acknowledgment sends `externalAccountIds.obfuscatedAccountId` and, when present, `obfuscatedProfileId` (not the GET schema's `obfuscatedExternal*` names); Google permits these write fields only for resubscriptions. When Google removes temporary context after acknowledgment, an already verified receipt can reuse its durable identifier through the original server binding. Explicit account/profile mismatches still fail, and an unverified candidate or current caller never supplies fallback ownership. Another signed-in user can trigger rightful-owner recovery, but receives a generic ownership conflict without the owner's identity/status. Reinstall and cross-device recovery depend on Google's identifier plus the server binding, not device journals.
- Each token has conditional writes; each Google root order (`..N` renewal suffix removed) has an immutable account claim. A linked replacement must have the same verified owner. Only an effective replacement supersedes the original. `PENDING` and `PENDING_PURCHASE_CANCELED` instead requeue the linked original without clearing its paid expiry or verification evidence. The superseded original is durably marked and cannot regain access through an old notification.
- Only after verified receipt evidence is durable does the server acknowledge Google and import the token into RevenueCat under the verified owner's opaque ID. RevenueCat import failure is durably recorded as `MirrorError`, logged without receipt/identity data, surfaced to the rightful user as `providerSyncPending`, and retried. It does not strand the rightful paid subscriber or grant an alias access.
- RevenueCat **Keep with original App User ID** remains required, but is defense-in-depth, not proof of first ownership. Do not manually transfer subscriptions or enable new-purchase S2S auto-attribution without separate review.

## Entitlement semantics

Effective Premium remains **manual account/email grant OR active Google-verified paid evidence**. The manual table is never changed. There are no new storage/AI quotas.

Active, cancelled-but-not-expired and Google grace-period states retain their verified expiry. Pending, on-hold, paused, expired and pending-purchase-cancelled states do not grant access even if a date is present. Only supported auto-renewing monthly/yearly base plans are accepted. Google test purchases cannot grant production access and vice versa.

For a currently paid period, `orders.get` also verifies the exact order ID, token and order state, requesting only those three fields (no buyer address/payment details). `REFUNDED`, `PARTIALLY_REFUNDED`, `CANCELED` and `PENDING_REFUND` remove access even if the subscription still reports a future expiry. Unknown/unprocessed orders fail verification. This intentionally revokes on any refund, including partial refunds and pending refunds; do not issue courtesy partial refunds expecting Premium to remain. A subsequent paid renewal can grant its new period.

Verified access lasts until the earlier of expiry and `MaximumStaleHours` (default 24 hours). This allows bounded provider-outage access and bounded refund staleness, not instant revocation. Requests read current durable receipt evidence, so a mirrored RevenueCat customer cannot manufacture Premium. A RevenueCat-only legacy paid row with no Google evidence is not trusted; there are no launched paid subscribers to migrate in this pre-launch implementation.

Status and sync responses reuse one paid snapshot for both `paidActive` and effective capabilities, while still checking manual Premium. An unchanged foreground snapshot needs one account read and one receipt query, with no account save and no postponement of the background reconciliation deadline. Changed evidence is persisted conditionally; conflicts reload both account and receipt evidence. The account's `PurchaseOrder` marker is committed with the immutable order claim, so a new order or a legacy row without that marker still checks ownership before the no-write path is available. Due background reconciliation advances its durable deadline even when the paid fields have not changed.

## Configuration

Provide only through approved host settings/secret mounts:

| Setting | Purpose/default |
| --- | --- |
| `Billing__Enabled` | `false`; billing binding/status/paid entitlement feature |
| `Billing__RecoveryEnabled` | `false`; Google verification, receipt recovery and acknowledgment |
| `Billing__CheckoutEnabled` | `false`; new sales only, requires recovery enabled |
| `Billing__Environment` | `PRODUCTION` or `SANDBOX`, never both |
| `Billing__SecretApiKey` | Optional existing RevenueCat server-only read/diagnostic capability; not used for paid entitlement or required for checkout |
| `Billing__AndroidPublicApiKey` | Actual `goog_...` key for this Google Play app's server receipt import |
| `Billing__AppId` | RevenueCat Google Play app ID, not package name |
| `Billing__WebhookAuthorization` | Exact RevenueCat webhook authorization, at least 32 characters |
| `Billing__KeepOriginalAppUserIdConfirmed` | `true` only after confirming the saved provider policy |
| `Billing__GooglePushAudience` | Exact HTTPS audience configured on the new authenticated Pub/Sub push subscription |
| `Billing__GooglePushServiceAccount` | Exact push-auth service account email, not a client-supplied value |
| `Billing__TableName` | `PremiumSubscriptions`; separate from manual Premium table |
| `Billing__ReconcileMinutes` | 15; allowed 1-60 |
| `Billing__MaximumStaleHours` | 24; allowed 1-48 |
| `Billing__WorkerFileLoggingEnabled` | `false` when absent; optional dedicated App Service filesystem worker diagnostics, independent of billing/recovery/checkout switches |
| Google Application Default Credentials | Backend-only host identity/federation or protected service-account secret mount with Android Publisher scope |

Google credential acquisition uses the existing Google auth dependency and `https://www.googleapis.com/auth/androidpublisher`. Grant the backend principal the required Play app permissions for subscription/order reads and acknowledgments. Uploading JSON into RevenueCat **does not provide backend credentials**. The isolated sandbox credential is described above; production credential provisioning remains a separate approved operation. Misconfigured credentials produce an explicit retryable failure; they never enable fake purchases.

Enabled settings validate webhook identity and restore policy. Recovery validates the public Android key and authenticated Google push settings. Checkout additionally requires recovery. After launch, stop new sales with `CheckoutEnabled=false`; leave `Enabled` and `RecoveryEnabled` on for existing subscribers. Disabled billing creates no tables/provider calls. Store construction is lazy; initialization failures remain retryable.

Prefer separate sandbox backend/storage and RevenueCat project/app. All receipt/binding/order namespaces include the environment. The frontend now supports an explicit reviewed sandbox HTTPS API origin even in production-mode builds.

## Durable notifications and reconciliation

**Google RTDN (new requirement):** add a second Pub/Sub subscription to the existing Play notification topic. Do not replace RevenueCat's topic/subscription. Use authenticated push to HTTPS `POST /api/billing/google/notifications` with the exact configured audience and service account. The endpoint verifies Google's signed JWT, audience, verified email and package. The Pub/Sub service agent must be able to mint the push identity. RTDN's token is persisted before HTTP 200; supplied state is never trusted. Duplicate notifications idempotently requeue the same token. This path can discover/recover a charge even if the app never resumes after the payment.

**RevenueCat webhook:** retain HTTPS `POST /api/billing/revenuecat/webhook`, exact secret authorization, expected app/environment and all subscription events. Its durable inbox stores minimal customer references and uses them to trigger Google re-verification of known tokens. A RevenueCat event cannot create ownership, change expiry or grant entitlement. Transfers cannot grant a new user access.

Both webhook paths are outside user write-capacity buckets and have independent authentication/body limits. Preserve trusted HTTPS forwarding on App Service; never blindly accept forwarded headers. Google recovery requests remain subject to normal authenticated write throttling.

The 30-second hosted worker pages up to 32 due receipts, 32 inbox events and 32 accounts on **every tick**, including when the startup queue was empty. Its receipt cursor survives individual row failures and failed page fetches, and resets only at the end of a scan; one receipt's provider/storage failure does not block the remaining page or the other queues. Cancellation flows through the loop and receipt I/O. Receipt evidence is verified with Google, persisted with ETags, acknowledged and mirrored. Pending tokens retry after one minute; ordinary receipts after `ReconcileMinutes`; failed reserved receipts retry after one minute. Superseded/expired tokens stop normal polling, but a new authenticated notification can reopen them for recheck. Token/ownership records are retained for deduplication and chain history.

**Operational requirements:** Azure App Service Always On; restricted Table access/backups (tokens are credentials); alert on eligible unacknowledged purchased receipts separately from still-pending payment, provider/credential failures, mirror attribution conflicts, RTDN delivery failures/dead letters and verification age. Logs suppress Google/RevenueCat HTTP clients and redact dependency URLs. Do not add request body, auth-header or database-entity logging. Google order responses request no buyer PII.

The current per-account receipt lookup is an Azure Table partition scan, and reconciliation is bounded rather than a high-scale scheduler. Before substantial subscriber volume, add an indexed account/receipt projection and queue workers, and size Google API quota: paid verification uses subscription and order reads plus mirroring. Backlog must not approach the freshness bound or Google's acknowledgment deadline.

## Worker monitoring

`BillingWorkerTick` (schema `worker_schema=2`) is emitted after each started tick,
including an empty tick, through `ILogger<BillingWorker>` and existing
`IUsageTelemetry`. The latter uses the application's existing Application Insights
destination **only if configured**. `billing_environment` distinguishes SANDBOX
from PRODUCTION independently of ASP.NET's hosting environment. No customer,
receipt, order, token hash, URL, provider response or exception text is emitted.

| Field | Exact meaning |
| --- | --- |
| `outcome` | `succeeded`: tick returned with no observed handled failures; `degraded`: storage/billing/conflict/mirror failures were observed; `cancelled`: shutdown interrupted this tick; `failed`: unexpected exception propagated. Disabled billing emits no ticks. |
| `duration_ms` | Elapsed tick duration, capped at one day. A hung tick emits no completion heartbeat. The 30-second timer does not guarantee completion every 30 seconds under load. |
| `{receipt,event,account}_page_read`, `_page_has_more` | Successful fetch of the queue's page and whether it returned a continuation cursor. A failed fetch is not an empty successful page. No extra monitoring Table queries are performed. |
| `{receipt,event,account}_attempted`, `_returned` | Operations attempted and returned without their direct failure. Receipt operations include event-triggered recovery and can repeat a receipt; they are not unique purchases, terminal completions, grants or acknowledgment counts. An internally handled mirror error can coexist with `receipt_returned`. Event reservation/completion ETag failures are not returned successes. |
| `{receipt,event,account}_failed`, `_storage_failure`, `_conflict`, `_billing_failure` | Handled failures, including page fetches. These are operation/page failure counts, not necessarily failed row counts. Nested receipt exceptions during an event are attributed to the enclosing event. Additional fixed `_credential_failure`, `_provider_failure`, `_ownership_failure` classifiers can overlap `_billing_failure`; never sum classifiers as distinct failures. |
| `receipt_pending`, `receipt_mirror_retry` | Returned receipts still awaiting payment and returned receipts with persisted mirror errors. Pending payment alone is not a failed tick. A mirror retry degrades the tick without changing verified entitlement. |
| `{receipt,event,account}_observed_due_age_seconds_max` | Maximum overdue time of attempted work, sampled **before** RetryAt is changed. It is neither the queue-wide oldest item nor cumulative retry age, since reservations/retries reset RetryAt. |
| `receipt_current_verified_observed`, `receipt_verification_age_seconds_max` | Count and maximum pre-attempt verification age for observed, previously verified, non-superseded receipts with future expiry. Zero with no matching observations is not evidence that all subscribers are fresh. |

Counts saturate at 1,000,000 per tick; ages at 365 days. Counters reset each tick.
Schema 2 adds the following **pre-attempt, observed-page** metrics; observations
can repeat tokens across receipt and event recovery. No additional Table query
or global scan was added. `coverage=observed_pages_not_population` is explicit.

| Field | Exact meaning |
| --- | --- |
| `receipt_ack_pending_observed`, `_unknown_observed`, `_confirmed_observed` | Eligible active/cancelled/grace receipts grouped by durable acknowledgment evidence before this attempt. Pending payments, terminal and superseded states are excluded. Missing legacy fields are unknown, not unacknowledged. |
| `receipt_ack_deadline_known_observed`, `_deadline_unknown_observed` | Among observed pending acknowledgments, whether a provider-start-based deadline estimate exists. Unknown is not healthy. |
| `receipt_ack_observed_age_lower_bound_seconds_max` | Time since this backend first persisted eligible Google evidence. This is only a lower bound on purchase age; it excludes any delay before discovery. |
| `receipt_ack_provider_start_age_seconds_max`, `_deadline_estimate_remaining_seconds_min`, `_deadline_estimate_overdue_observed` | Age from Google's subscription grant `startTime`, minimum remaining **estimated** deadline and count already past it. Remaining seconds may be negative (overdue); an absent measurement means unknown/no qualifying observation, never zero remaining or healthy. |
| `receipt_ack_retry_age_seconds_max`, `_attempts_max` | Time since first durable acknowledgment reservation and maximum lifetime reservations for observed unacknowledged receipts. A reservation may precede a crash before network dispatch. |
| `receipt_ack_attempt_reserved`, `_response_succeeded`, `_confirmation_persisted`, `_unconfirmed`, `_provider_read_confirmed` | Current tick reservations, successful HTTP responses, durable response confirmations, ambiguous failures (including persistence/cancellation), and acknowledged provider reads. These overlap; they are not distinct purchase counts. Provider-read confirmations include repeats. |
| `receipt_retry_cycle_observed`, `_retry_cycle_age_seconds_max`, `_retry_cycle_attempts_max`, `_retry_history_unknown_observed` | Pre-attempt durable age/count of an incomplete recovery cycle (including mirror failure); absent legacy retry history is counted explicitly. A successfully handled pending-payment check ends a cycle and does not count as a payment failure. |
| `{receipt,event,account}_scan_completed`, `_scan_elapsed_seconds`, `_last_scan_completed_age_seconds` | Current process's due-query pagination progress: reaching a null continuation marks a completed fetch traversal, including empty pages. Failed page reads do not advance completion. Last-completed age is absent before first traversal and resets on process restart. This is **not** successful processing of all rows, a snapshot, full population coverage, or a bound on skipped/newly due work. |

#### Durable acknowledgment evidence and deadlines

Google's actual GET field is **`acknowledgementState`** (British spelling):
`ACKNOWLEDGEMENT_STATE_PENDING` or `ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED`.
Missing/unspecified state remains unknown and prevents acknowledgment dispatch;
unknown enum values fail response validation. `startTime` is Google's subscription
grant time, not `lineItems.expiryTime`, receipt arrival or renewal timestamp.
The API exposes **no exact acknowledgment-deadline timestamp**.

Only after environment, immutable owner/linked-chain and order checks succeed,
the receipt persists `EligiblePurchaseObservedAt`, optional `ProviderStartedAt`,
`AcknowledgmentDeadlineEstimateAt` and `AcknowledgmentState`. New pending payments
never start that clock, even if a stray date is present. The estimate is grant
time plus Google's documented three-day production window, or three minutes for
license-test sandbox purchases. It is not a provider-returned contractual deadline:
subscription start and purchase-completion semantics, discovery delay and test
acceleration must be considered. Missing/future start leaves the estimate unknown.
An earlier authoritative start can tighten an estimate; a later read cannot extend it.
Do not use this estimate to stop recovery or grant/revoke Premium.

`AcknowledgmentFirstAttemptAt`, `AcknowledgmentLastAttemptAt` and saturating
`AcknowledgmentAttempts` are conditionally persisted before dispatch. A successful
acknowledgment response is persisted **before RevenueCat import**.
`AcknowledgmentConfirmedAt` is the local time confirmation was observed, not
Google's actual acknowledgment time; `AcknowledgmentConfirmationSource` is
`ack_response` or `provider_read`. HTTP timeout or failure to save a successful
response leaves durable unconfirmed work. A later Google GET can confirm an
already acknowledged purchase without another acknowledgment call. Owner/environment
checks still run before accepting that evidence. No failure transfers ownership.

`RecoveryCycleStartedAt` / `RecoveryCycleAttempts` are reserved before I/O and
survive redelivery/restarts until full recovery (including mirror) returns.
`RecoveryAttempts` is a lifetime reservation count. Existing rows migrate lazily
on their ordinary recovery path; old completion times and attempts are **not**
backfilled from `VerifiedAt`, Table Timestamp or RetryAt. Already completed
unvisited rows retain unknown history. Historical acknowledgment evidence remains
on terminal/superseded rows but is excluded from outstanding acknowledgment metrics.

Privacy-safe `BillingAcknowledgment` phase logs use the same dedicated worker
category for HTTP-triggered and worker recovery; they contain only fixed phase
and environment codes. They do not call identity-attributing usage telemetry
from a request context. Worker counters feed `BillingWorkerTick`. Known logger/
telemetry sink failures are diagnosed with fixed trace text and do not replace
purchase/provider failures or stop acknowledgment.

Sources: [Google SubscriptionPurchaseV2](https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.subscriptionsv2),
[pending purchase acknowledgment window](https://developer.android.com/google/play/billing/integrate#pending),
[license-tester acknowledgment](https://developer.android.com/google/play/billing/test).

Whole-queue oldest work, pending-payment duration, complete unacknowledged
population and full-population freshness remain unestablished. Before sales,
approve an indexed projection/queue design if population coverage is required;
bounded pagination progress must not be presented as complete coverage.

### Sandbox activation

The latest acknowledgment-monitoring deployment on 2026-09-26 is
`ffe8835bcd924885b60490d1e69a8c45` (ZIP SHA-256
`4d5e09fe6052dc739429c014389071940d7508b7f08f09cde97fc0e160b16d56`;
API assembly SHA-256 `1171fd50423b96282c7009ff16044b7c541e5e53e5a17eb2800be432c329607c`).
All 104 packaged files matched the live site. It retains the previous sandbox
appsettings bytes, every host/logging setting, protected credential path and
.NET 10.0.11 worker. No new Azure resource or ingestion/notification route was
created. An initial verification/rollback-submission transport interruption was
resolved with read-only deployment/hash/configuration checks: the new package
remained active, and no rollback deployment was accepted.

Schema 2 logs accumulated 85 successful ticks from 11:00:38 through 11:42:37 UTC
during deployment verification, with nine account reconciliations and zero
receipt/event/acknowledgment attempts. These prove the new schema and live
bounded polling, **not** a new acknowledgment lifecycle or population health.
Unknown/no-observation acknowledgment ages were absent, not healthy zero values.
Failure, timeout-after-dispatch, persistence conflict, already-acknowledged reads,
pending/terminal/superseded states, legacy history and sink failures were exercised
only with controlled regressions. Evidence is in the session's
`billing-acknowledgment-monitoring\outcome.json`.

The separately approved monitoring deployment on 2026-09-26 is
`41cecfc430724b4c8db455152a981217` (ZIP SHA-256
`194ece7d1b88b8977696edda59354361eca1db2f72d43ca9049adea765468f7b`).
It retains .NET 10.0.11 in-process hosting and the existing nonsecret sandbox
appsettings bytes. Only `Billing__WorkerFileLoggingEnabled=true` was added to
host settings and application filesystem logging changed from Off to Information.
HTTP logs, detailed errors, failed-request tracing and blob logging remain off.
Billing stays true / true / true in SANDBOX; credentials, notifications, storage,
Always On, production and the shared compute plan are unchanged.

The official `Microsoft.Extensions.Logging.AzureAppServices` provider was added
because the previous dependency graph had no App Service filesystem provider.
Its file sink accepts only the exact BillingWorker category, Information or above,
with scopes disabled. Dedicated `teamify-billing-worker-*.txt` files under
`C:\home\LogFiles\Application` retain at most three daily files, with a 16 MiB
per-file cutoff. This is bounded operational diagnostics, not durable history;
the provider can drop messages if its queue/file limit is reached. No new Azure
resource, App Insights destination, alert recipient or recurring resource charge
was introduced; this uses existing App Service filesystem capacity.

Disable only this sink with `Billing__WorkerFileLoggingEnabled=false` and return
application filesystem logging to Off if no other approved consumer uses it.
Do **not** disable billing or recovery to silence diagnostics. App Service
filesystem logging can automatically turn off after its platform diagnostic
window; the custom switch alone does not keep the platform sink enabled. Recheck
the logging level when collecting evidence. The dedicated files are retrieved
through authenticated SCM with a schema/numeric allowlist, never a full log dump.

Live observation and its limits are recorded in the session's
`billing-sandbox-monitoring\observed-ticks.json` and `outcome.json`. Four distinct
successful live ticks were observed at 09:30:00, 09:30:29, 09:30:59 and 09:31:29 UTC
on 2026-09-26 (approximately 30 seconds apart, 29-320 ms elapsed). Each read all
three queue pages with zero due items and no continuation/failure. Empty successful
ticks prove live queue polling, not a new receipt lifecycle or alert delivery.
The later bounded window (09:47:59-10:37:29 UTC) contains 100 successful ticks and
12 naturally scheduled account reconciliations returned without failure. Receipt
and event attempts were zero in that window; it does not establish new Google
verification/acknowledgment or RevenueCat import work.
Provider/storage/mirror failure, retry, cancellation, propagation and event ETag
classification are exercised only by controlled regressions, not induced cloud
failures. **No alerts are installed by this log activation.**

## Implementation and local regression

- `Premium\PlayPurchaseClient.cs`: Google credential acquisition, subscription/order validation, acknowledgment and supported RevenueCat import.
- `Premium\PlayPurchaseRecovery.cs`: original-owner resolution, durable evidence, replacement revocation, acknowledgment/import sequencing.
- `Premium\PlayPurchaseStore.cs`: durable receipt inbox, immutable order claims and replacement markers.
- `Controllers\PlayBillingController.cs`: verified-user recovery and authenticated Pub/Sub notification endpoint.
- `Premium\BillingService.cs`, `BillingWorker.cs`: canonical entitlement aggregation and durable reconciliation.
- Existing `AccountEntitlements` manual OR paid behavior remains unchanged.

```text
dotnet run --project scripts\RatingRegression\RatingRegression.csproj -- --billing
```

The existing runner covers Google/RevenueCat schemas, ownership and linked chains, wrong-account first recovery, pending completion, mirror outage after charge, refund/grace/hold, environment separation, manual compatibility, controller authentication, durable storage/ETags and initialization retry. Doubles do not establish store/service-account interoperability.

`BillingReviewRegression` exercises the actual worker loop with controlled ticks (empty startup, later delivery, scheduled verification, pagination failures and cancellation), actual recovery for a canceled pending replacement with a 20-day paid original, and the real Google HTTP client/parser/acknowledgment plus recovery across context removal. Provider HTTP responses are controlled, not live. It checks the exact acknowledgment payload, post-24-hour refresh, account/profile mismatch refusal, unknown-owner and unverified-candidate refusal, and the supported RevenueCat `/receipts` mirror. Table-store regressions also cover identifier persistence and non-destructive original requeue.

## Outstanding launch work

Play monthly/yearly base plans, RevenueCat Play offering/entitlement mapping, sandbox backend permissions, authenticated RTDN/webhook delivery and license-test purchase/receipt import have been exercised. Merchant/bank verification is still unconfirmed. The supported production .NET 10 backend is deployed with billing disabled. Production-specific credentials, notification delivery, Always On, operational alerting and a separate production Android artifact remain outstanding; follow the rollout gates in [the production runbook](premium-production-rollout.md).

Reported phone scenarios include monthly/yearly purchases, cancellation/expiry, build 216 declined-payment retry followed by an approved purchase, wrong-Teamify-account refusal with the original account retaining access, app reopen continuity, checkout Back/cancel retry, offline-before-checkout recovery and background/resume. An app reopen is not a fresh-install restore, and offline preflight is not network loss after payment submission.

Submitted-purchase process death and fresh-install/second-device restore were explicitly **skipped by the user**, not passed. Explicit Restore clicks on each account and exact live pending/renewal/grace/hold/refund/linked-replacement behavior remain separately unverified. Controlled regression coverage does not establish those device/provider outcomes. Unknown-owner historical/out-of-app purchases are intentionally not auto-assigned. A checkout crash without a queryable token can conservatively block another in-app sale indefinitely, without blocking logout or existing paid access; there is no approved production reset.

Signed internal sandbox build 216 (`20.11-billing-retry`) was installed through Play and exercised with license-test payment methods. It is not a production artifact. The separate runtime rollout deployed the production backend and approved host runtime/billing-off flags, but did not enable billing, sign a production build or change provider settings.

## References

- https://developer.android.com/google/play/billing/integrate
- https://developer.android.com/google/play/billing/security
- https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.subscriptionsv2/get
- https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.subscriptions/acknowledge
- https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.subscriptionsv2 (pending-purchase cancellation and temporary out-of-app context)
- https://developers.google.com/android-publisher/api-ref/rest/v3/orders/get
- https://developers.google.com/android-publisher/api-ref/rest/v3/orders
- https://androidpublisher.googleapis.com/$discovery/rest?version=v3 (canonical order/refund schema)
- https://cloud.google.com/pubsub/docs/authenticate-push-subscriptions
- https://www.revenuecat.com/docs/api-v1/transactions
- https://www.revenuecat.com/docs/migrating-to-revenuecat/migrating-existing-subscriptions
- https://raw.githubusercontent.com/RevenueCat/purchases-android/10.20.0/purchases/src/main/kotlin/com/revenuecat/purchases/PostPendingTransactionsHelper.kt
