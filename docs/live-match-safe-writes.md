# Shared match and saved-teams write contract

Write contract **2** protects the revision the user actually edited. The
`client_version` header remains telemetry, not an authorization to overwrite.

## Match rows

`POST Teams/ReadMatches`, `AddMatch`, `EditMatch`, `DeleteMatch`, and
`DoneAndReadMatches` retain their existing array response shape. Each row includes
an opaque string `revision`, alongside `partitionKey`, `rowKey`,
`serializedMatch`, `createdAt`, `timestamp`, and the legacy `eTag` field.

`POST Teams/EditMatch` and `POST Teams/DeleteMatch` require:

```json
{
  "partitionKey": "the-loaded-matchday",
  "rowKey": "the-loaded-game",
  "serializedMatch": "{\"teamA\":{\"score\":1},\"teamB\":{\"score\":0}}",
  "writeContractVersion": 2,
  "expectedRevision": "the-exact-revision-originally-loaded-into-this-edit"
}
```

The revision is an HTTP entity-tag string; retain its quotes/weak-tag prefix
verbatim. Do not replace an active edit's expected revision when polling history.
Deletion does not require `serializedMatch`.

- A missing/unsupported contract, missing revision, or wildcard revision returns
  **428** with `success:false`, `requiresUpgrade:true`,
  `errorCode:"safe_write_upgrade_required"`, and an upgrade/reload message.
- A stale revision returns **409** with `isConflict:true` and authoritative
  `matches`, including their revisions. Preserve the draft, show the conflict,
  and let the user explicitly reload/reconcile. Never silently retry with a
  freshly fetched revision.
- Existing closed-matchday and not-found responses are preserved. The atomic
  matchday-status guard remains in the transaction. Only contention on status
  alone may be retried; a changed match revision is never retried.
- `AddMatch`, `AddPlayerSwap`, `StartScoreboard`, and `DoneAndReadMatches`
  deliberately retain the legacy protocol: no version/revision fields or new
  started-day requirement. Their existing atomic guards remain unchanged.
- Legacy `AddMatch` remains an atomic create-only operation. Retrying the same
  `(partitionKey,rowKey)` conflicts rather than merging scores. New games need
  distinct stable IDs. No global first-to-two rule is introduced.
- Residual legacy risk is intentional: an old client using cached wrong-day
  context can still add/start/close or record a swap against that supplied day,
  subject to the existing closure/duplicate guards. This release does not claim
  to fix that legacy routing defect. Upgraded clients must bind writes to the
  authoritative opened route, never a cached fallback. The API does not invent
  a current-day registry or reject explicitly opened historic still-open days.

## Current generated-teams snapshot

This blob is a generated-teams array wrapper, **not a matchday registry or
historical-day pointer**. Its existing owner/group path and JSON storage shape
remain unchanged.

`POST Teams/GetTeamsFromStorage?uid=...&groupId=...` adds `revision`, `exists`,
and `writeContractVersion:2` to its existing response. Missing blobs return
`exists:false`, `revision:"absent"`, and the existing `success:false` shape.
Other storage errors are not translated into a missing snapshot.

`POST Teams/SaveToStorage?uid=...&groupId=...` requires:

```json
{
  "teams": [],
  "writeContractVersion": 2,
  "expectedRevision": "the-revision-loaded-before-editing"
}
```

Use `"absent"` only when creating a snapshot that does not exist. The server
performs `If-None-Match:*` for creation and `If-Match:<expectedRevision>` for
replacement. Success returns `success:true`, `revision`, and
`writeContractVersion:2`. Stale writes return **409** with `isConflict:true`,
`errorCode:"stale_revision"` and a reload/review message. Legacy unconditional
saves return **428**; the old connector entrypoint also refuses them.

Existing raw blobs need no content migration: reads return their current ETag
and the first safe write conditionally replaces that same blob. Write metadata
is not persisted into the teams JSON. Shared/co-organizer responses also retain
revision metadata while preserving player-field redaction.

Editing a historical match must call the match-row API for its explicit day;
it must not call `SaveToStorage` to replace current generated teams. This API
does not create a historical lineup store or infer a day from team arrays.

## Rollout

Coordinate the API and both website hosts, plus Android clients. The legacy
shared-link host must receive the revision-aware UI too. Deploying backend first
intentionally rejects unsafe legacy edits/deletes and teams overwrites with 428;
deploying UI first against an older backend does not provide concurrency safety.
Clients must preserve drafts and handle 409/428 explicitly.
Legacy Add/Start/Done/player-swap calls remain available, as explicitly chosen
for old Android compatibility. They do not require the upgraded provider to send
a version field. This does not relax the already-disclosed 428 protection on
revisionless Edit/Delete or unconditional SaveToStorage.

No match-history rewriting, private configuration, migration job, or new
dependency is required. Targeted regression entrypoint:

```powershell
dotnet run --project scripts\RatingRegression\RatingRegression.csproj -- --live-match-concurrency
```

The regression uses in-memory transactional Tables and loopback-only Blob SDK
requests, including controlled delayed writes, closure races, legacy clients,
and existing-blob compatibility.
