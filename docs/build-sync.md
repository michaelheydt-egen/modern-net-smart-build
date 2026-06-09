# Build sync — opt-in SQLite mirror of Jenkins build history

`cicd-build` history (and the per-build summaries the WebUI shows) can be mirrored to a local SQLite database so the UI:

- loads faster (in-process SQL read vs HTTP round-trip to Jenkins)
- retains build history past Jenkins's `logRotator` policy
- can survive Jenkins restarts / reachability blips with cached state

It is **off by default**. The WebUI ships in live-Jenkins mode — every call goes straight through to Jenkins via [JenkinsLiveBuildStore](../src/web-admin/cicd.web.admin/Services/Builds/JenkinsLiveBuildStore.cs). Flip the switch in config when you want the persistence.

## Enabling

`appsettings.json`:

```jsonc
"BuildSync": {
  "Enabled": true,
  "Jobs": [ "cicd-build" ],     // jobs to mirror
  "PollIntervalSeconds": 60,
  "BackfillCount": 100,         // last-N builds fetched on first run
  "PerJobFetchCount": 25,       // builds fetched per tick steady-state
  "DbPath": "./data/builds.db", // relative paths resolve to the WebUI's CWD
  "ReconcileDeleted": false     // see "Reconciling Jenkins-side deletes" below
}
```

…or via env var: `BuildSync__Enabled=true`, `BuildSync__DbPath=/data/builds.db`, etc.

On startup with `Enabled=true`:

1. The DB path's parent directory is created if it doesn't exist.
2. Pending EF Core migrations are applied (`db.Database.MigrateAsync()`).
3. [BuildSyncService](../src/web-admin/cicd.web.admin/Services/Builds/BuildSyncService.cs) kicks off and runs forever as a `BackgroundService`. First tick is immediate (so the UI is populated within seconds); subsequent ticks honor `PollIntervalSeconds`.

## Docker compose — bind mount

The DB file is in-process / file-based, so the container needs read+write access. Bind-mount a host directory so the data survives container rebuilds:

```yaml
services:
  cicd-web-admin:
    image: cicd-web-admin:latest
    environment:
      - BuildSync__Enabled=true
      - BuildSync__DbPath=/data/builds.db
    volumes:
      - ./cicd-web-admin-data:/data    # host dir → /data inside container
    ports:
      - "8080:8080"
```

Why a bind mount over a named volume: easier to back up, `sqlite3` inspection on the host, and matches the user's stated preference.

## Schema

One table, intentionally denormalized — causes / artifacts / build-info.json live as JSON columns. See [BuildRunRecord.cs](../src/web-admin/cicd.web.admin/Services/Builds/BuildRunRecord.cs).

```text
BuildRuns
├─ Id                INTEGER PK
├─ JobName           TEXT
├─ Number            INTEGER
├─ Result            TEXT  (Success / Failure / Aborted / ..., null = in-flight)
├─ Building          BOOLEAN
├─ Timestamp         INTEGER  (unix ms)
├─ Duration          INTEGER  (ms)
├─ Description       TEXT
├─ CausesJson        TEXT  (JSON array of shortDescription strings)
├─ ArtifactsJson     TEXT  (JSON array of {fileName, relativePath})
├─ BuildInfoJson     TEXT  (verbatim build-info.json blob)
├─ SyncedAt          INTEGER  (unix ms)
└─ UNIQUE INDEX (JobName, Number)
└─ INDEX (JobName, Building)
```

Promote any of those JSON columns to real child tables only when an actual query starts needing it.

## Sync semantics

Per tick, per configured job:

1. `ListBuildsAsync(job, PerJobFetchCount)` — single HTTP call.
2. Diff against rows we already have at those numbers.
3. For each new or still-`Building` row: `GetBuildDetailsAsync` for the artifacts + causes, then `GetArtifactAsync("build-info.json")` if the build archived one. Failures of either are logged at `Debug` / `Warning` and don't fail the tick — the row gets stored with whatever level of detail we managed to fetch.
4. Refresh any `Building=true` rows from previous ticks that aren't in the recent window (long-running builds outside `PerJobFetchCount`).
5. `SaveChangesAsync` once, at the end.

First tick for a job uses `BackfillCount` instead of `PerJobFetchCount` so a fresh DB doesn't start empty.

### Live fallback

Even with sync on, the [SqliteBuildStore](../src/web-admin/cicd.web.admin/Services/Builds/SqliteBuildStore.cs) falls back to a direct Jenkins read when a requested build isn't in the DB yet (e.g. someone clicks into a build that just landed and the sync hasn't picked it up). No UX regression vs live mode.

## Adding more jobs to the mirror

Just append to `BuildSync:Jobs`. The next sync tick picks them up — first one for any new job will hit `BackfillCount`. No migration needed.

## Adding new fields

Touch `BuildRunRecord` (entity) and `BuildSyncDbContext.OnModelCreating` (only if you need an index), then:

```bash
dotnet ef migrations add <Name> \
  --project src/web-admin/cicd.web.admin/cicd.web.admin.csproj \
  --output-dir Services/Builds/Migrations
```

On the next deploy the WebUI applies the migration at startup.

## Inspecting the DB

```bash
sqlite3 ./cicd-web-admin-data/builds.db
sqlite> SELECT JobName, COUNT(*) FROM BuildRuns GROUP BY JobName;
sqlite> SELECT Number, Result, Description FROM BuildRuns
        WHERE JobName='cicd-build' ORDER BY Number DESC LIMIT 10;
```

## Reconciling Jenkins-side deletes

Off by default. When `BuildSync:ReconcileDeleted = true`, each tick — after the upsert pass — deletes any local rows whose `Number` falls **inside the window Jenkins just returned** but that Jenkins didn't actually include. Typical trigger: an admin deleted a recent build in the Jenkins UI and you don't want the orphan lingering in `/jenkins/builds`.

Scope is intentionally bounded to the current fetch window — older history (which exists in the mirror but is no longer visible from Jenkins's recent-N) is **not** touched. The whole point of the mirror is usually to outlast Jenkins's retention; mass-pruning local rows whenever they age out of Jenkins's window would defeat that. If you want a global reconcile, the right shape is a separate maintenance job — say so in an issue when you actually need it.

## Surface in the UI

A **"Sync"** pill in the app bar (next to the Jenkins health badge):

| State | Pill | Tooltip |
| --- | --- | --- |
| `BuildSync:Enabled=false` | grey *Sync: live* | "Live mode — every read goes straight to Jenkins…" |
| Sync on, no error, healthy | green *Sync: 12s ago • 187* | last-sync timestamp + builds tracked |
| Sync on, last tick failed | yellow *Sync: error* | the error message from the last attempt |
| Sync on, before first tick | grey *Sync: pending* | "Sync service starting — first tick pending" |

Click it to land on `/settings` — read-only view of the current mode, status, and full config. Changing the mode still requires a config edit + WebUI restart; an in-UI toggle would mean persisting runtime state somewhere and isn't worth the extra moving piece for an opt-in feature.
