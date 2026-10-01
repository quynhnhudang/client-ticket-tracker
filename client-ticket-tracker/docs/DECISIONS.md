# Design decisions and disclosures

A running log, one entry per decision: what was chosen, the alternative, and why.
The last section lists every corner cut, so you know what to disclose if asked.
The code comments explain the same points next to the code they apply to.

---

## Changes from the approved plan

| Plan said | Built instead | Why |
|---|---|---|
| Dapper for the hand-written query | Plain ADO.NET (`SqlCommand`, `SqlParameter`, `SqlDataReader`) | No extra dependency, and nothing hidden: you can explain every line, including why each parameter has an explicit type and size. Dapper would mostly save the reader-mapping loop. |
| CORS configured for development | No CORS at all | The Angular dev server proxies `/api` to the API (`web/proxy.conf.json`), so the browser sees one origin in development as well as in production. Same-origin keeps the auth cookie `SameSite=Strict` and removes a configuration difference between environments. |
| `TicketComments.IsInternal` flag | Dropped; all comments are internal | There is no client-facing channel, so the flag would have been a column that does nothing. Easy to add back if there's ever a client portal. |
| (open question) Clients as columns or a table | Separate `Clients` table, unique on email | More normalised, and it gives the search query a real JOIN. Find-or-create by email happens on intake. |
| Index keyword search on `(Title, Description)` | Index a computed `SearchText = Title + ' ' + Description` | SQL Server evaluates `CONTAINS((A, B), 'x AND y')` per column: both words must be in the *same* column. Found while designing the search; see "Search" below. |
| (not planned) | Login rate limiting, a health endpoint, an `IEscalationNotifier` seam | Each is small. Rate limiting closes a gap I'd otherwise have to disclose. The health check is used by the deploy script. The notifier let email be added without touching `TicketService`. |
| SendGrid free tier or Mailtrap | Provider-neutral SMTP; Mailtrap for testing | SendGrid replaced its free plan with a 60-day trial in March 2025. Plain SMTP means any provider is a configuration change. |
| SQL Server placement undecided | SQL Server 2025 **Express** on the same EC2 instance (t3.medium); RDS documented as the alternative | One box shows every piece and keeps costs down. Express rather than Developer, because Developer's license doesn't allow production use. |

---

## Platform
- **.NET 10 (LTS), ASP.NET Core controllers.** LTS means 3 years of support. Controllers rather than minimal APIs because `[ApiController]` gives automatic model validation and consistent 400 responses, and the structure is familiar to most .NET teams.
- **EF Core for schema and CRUD, raw SQL for search.** Migrations keep the schema in source control, next to the code that depends on it. Search is the one query that benefits from hand-tuning (full-text, UNION ALL, explicit parameter types), so it drops below the ORM.

## Data model
- **Enums stored as ints**, protected by CHECK constraints. Strings would be more readable in the table, but `ORDER BY Priority` would then sort alphabetically.
- **`DateTimeOffset` everywhere.** A `DateTime` read from `datetime2` comes back with `Kind = Unspecified`, serialises without a `Z`, and the browser shows it in the wrong time zone.
- **`RowVersion` for optimistic concurrency.** If two agents change the same ticket at once, the second save fails with 409 instead of silently overwriting the first (the "lost update" problem). This is optimistic concurrency: no locks are held while an agent is looking at the ticket.
- **Status history in the same transaction.** One `SaveChanges` call covers the ticket UPDATE and the history INSERT, and EF wraps that in a transaction, so history can't disagree with the ticket.
- **All foreign keys `Restrict`.** No cascading deletes of audit history. It also avoids SQL Server's "multiple cascade paths" error.
- **Unique index on `Clients.Email`, plus a retry.** The index is what actually prevents duplicates: two requests can both pass an "exists?" check in code. The retry turns the losing request's error into a success.

## Status workflow
- The allowed transitions are a table in `TicketStatusRules` (pure code, unit tested). The API sends `allowedNextStatuses` to the UI, so the rules live in one place.
- Status changes are `POST /tickets/{id}/status`, not a PATCH of a field: the change has side effects (history, email), and there's no generic update endpoint that could skip the rules.
- Escalation requires a note (it becomes the email body in Stage 3). Resolving requires resolution notes.

## Search (`Data/Queries/TicketSearchQuery.cs`)
- **One box, three behaviours:** a whole number is an ID lookup (a clustered-index seek). Otherwise, client-name prefix OR keywords.
- **`LIKE 'prefix%'` for names:** sargable, meaning it can seek `IX_Clients_Name`. `'%text%'` can't use the index. Trade-off: "Corp" doesn't find "Acme Corp".
- **Full-text `CONTAINS` for keywords** instead of `LIKE '%word%'`, which reads every row. Every word must appear (AND), each matched as a prefix (`"time*"` matches "timeout").
- **`UNION ALL` of two branches instead of `OR`:** each branch uses its own index; `OR` across two tables usually forces a scan. `db/explain-search.sql` shows both.
- **Parameters with explicit `SqlDbType` and fixed size:** avoids type-guessing conversions, gives one cached plan instead of one per input length, and a size large enough never to truncate.
- **Parameterisation is not the whole story:** the value is still interpreted by LIKE (`%`, `_`, `[`) and by the full-text parser (quotes, `NEAR`, `AND`). `SearchInput` escapes wildcards and rebuilds full-text terms from letters and digits only.
- **Graceful fallback:** full-text is an optional SQL Server component. The app checks once for the index and uses the LIKE query if it's missing. The response's `mode` field says which query ran.
- **`STOPLIST = OFF`:** with the default stoplist, a search containing a noise word like "the" can return nothing.

## Auth
- **Cookie, not JWT:** the API and UI share one origin, so an HttpOnly cookie works, and JavaScript (including injected JavaScript) can't read it. A JWT in localStorage can be stolen by any XSS bug.
- **`SameSite=Strict`** is the CSRF defence: the browser won't attach the cookie to requests started by another site.
- **Fallback authorization policy:** every endpoint requires login unless marked `[AllowAnonymous]`, so a new endpoint can't be left open by accident.
- **`PasswordHasher<T>`** (PBKDF2 with salt) without the rest of ASP.NET Identity: there's one role and no sign-up, so Identity's tables and UI would be unused weight.
- **Constant-time-ish login:** unknown emails still run a hash verification, so response time doesn't reveal which emails exist. Both failures give the same message.
- **Rate limiting:** 5 login attempts per minute per IP, with the IP taken from `X-Forwarded-For` because nginx sits in front.
- **Initial agent from configuration**, not seeded in a migration, so no credential ever reaches Git.

## Frontend (Angular 22, `web/`)
- **Standalone components, signals, and the built-in control flow** (`@if`, `@for`). This is current Angular; no NgModules. Angular 22 is also zoneless by default: change detection runs when signals change or events fire, not after every async callback.
- **The URL is the state for the queue:** filters and page are query parameters bound to component inputs (`withComponentInputBinding`). So views can be bookmarked and shared, and the back button works.
- **`switchMap` for loading:** when the filters change mid-request, the old request is cancelled, so a slow stale response can't overwrite a newer one.
- **The server decides the workflow:** the detail page renders buttons from `allowedNextStatuses`. There's no copy of the transition table in TypeScript to drift out of sync. Client-side checks (e.g. "escalation needs a note") exist only for instant feedback; the server enforces them.
- **Auth:** `/api/auth/me` runs at startup (`provideAppInitializer`), so a refresh keeps you logged in. The guard is UX only; the API's fallback policy is the real protection. A functional interceptor sends any 401 to the login page with a `returnUrl`, which is validated to be an in-app path so it can't be used as an open redirect.
- **Lazy-loaded routes:** each page is its own JavaScript chunk.
- **No UI library:** plain CSS in one file, so every style is yours to explain. It also keeps the bundle small (about 90 KB transferred on first load).
- **XSS:** Angular's `{{ }}` interpolation always escapes, and nothing uses `innerHTML`, so ticket text containing `<script>` shows as text (checked in the browser test).

## Escalation email (`email/README.md` has the full table)
- **Table layout, inline styles, no `<style>` block**, plus Outlook-specific pieces: a ghost table for the 600px width, and a VML button.
- **A pure builder:** `EscalationEmailBuilder` has no SMTP, database or clock, so it's unit-tested, and the sample file was produced by it.
- **The template is an embedded resource**, so it can't go missing from a deployment.
- **Everything is HTML-encoded, and the subject is stripped of CR/LF.** Client text can't inject links or headers.
- **multipart/alternative** (HTML + plain text) via MailKit, the library Microsoft recommends instead of `SmtpClient`.
- **Email is off unless `Email:Host` is set**, and when it's on, the settings are validated at startup (`ValidateOnStart`). A misconfiguration fails at deploy time, not on the first escalation.

## LLM triage (optional)
- **Called from the server** (typed `HttpClient` via `IHttpClientFactory`), so the API key never reaches the browser.
- **Suggest, never apply:** the UI pre-fills the dropdowns with a "suggested" tag, and the agent must click Create.
- **Model output is untrusted input:** it's parsed and validated against the enums; anything else is discarded (`TriageResponseParser`, unit-tested with hostile outputs).
- **Prompt injection is contained, not prevented:** ticket text is wrapped in tags (with `<`/`>` escaped) and the model is told to treat it as data. Because the output is validated and only suggested, the worst case is a bad suggestion that a person reviews.
- **Timeouts, rate limits and graceful failure:** 10-second timeout, 20 calls per minute per agent, and on failure intake works exactly as without the feature.

## Deployment (`deploy/`)
- **nginx serves the static Angular files and proxies `/api` to Kestrel on 127.0.0.1:5080.** Kestrel and SQL Server listen only on loopback.
- **systemd runs the app** as a no-login `tickettracker` user, restarts it on crash, and applies sandboxing (`ProtectSystem=strict`, `NoNewPrivileges`). Secrets come from a root-owned `EnvironmentFile`.
- **Build on the server, release by symlink:** each deploy builds into a new timestamped folder, then atomically repoints `current` (rename, not delete-and-recreate). A failed build never touches the live site. A failed health check rolls back automatically, and `--rollback` does it by hand. The last 5 releases are kept.
- **Migrations bundle** (`dotnet ef migrations bundle`), built from the same commit, run with a separate **db_owner "migrator" login**. The app's own login can only read and write rows.
- **Caching:** hashed JS/CSS cached for a year (`immutable`); `index.html` is `no-cache`, so a deploy takes effect on the next page load.
- **`X-Forwarded-For $remote_addr`** (overwrite, not append), so clients can't spoof their IP to dodge the login rate limit.

## Running behind nginx
- `UseForwardedHeaders` trusts `X-Forwarded-For`/`-Proto` only from loopback (nginx on the same machine).
- No `UseHttpsRedirection`: nginx owns HTTPS.
- Data Protection keys can be persisted to a directory (`DataProtection:KeysPath`), so restarts don't log everyone out.

---

## Shortcuts and known gaps (disclose these honestly)

1. **Escalation notification is fire-once.** It runs after the commit; if it fails, it's logged but not retried. Production-grade: a transactional outbox (write an "email to send" row in the same transaction, and a background worker sends and retries).
2. **Full-text indexing is asynchronous.** A new ticket can take a few seconds to show up in keyword search. ID and client-name search are immediate.
3. **The full-text check is cached for the process lifetime.** Restart the API after running `db/fulltext.sql`.
4. **All keyword terms must match (AND), and there's no relevance ranking.** Results are ordered client matches first, then newest. `CONTAINSTABLE` would give a RANK column.
5. **Search returns at most 50 results, with no paging.**
6. **Queue paging uses OFFSET/FETCH**, which gets slower on deep pages. Keyset paging ("rows after this one") scales better but is more complex.
7. **Reopening keeps the old resolution notes**, and resolving again overwrites them. The previous notes aren't kept in the history.
8. **An existing client's name isn't updated** if intake uses a different name with the same email.
9. **The concurrency (409) path has no automated test**, because it needs a real database and two overlapping requests. The unit tests cover only the pure logic; there are no integration tests against SQL Server yet.
10. **No password reset, account lockout or multi-factor**, and just one seeded agent (no UI to add agents).
11. **`SearchText` is a hand-managed column outside EF's model.** A future migration that changes `Title` or `Description` would need to drop and recreate it (and the full-text index) around the change.
12. **Local development uses `sa`. Production uses least-privilege logins, but still `TrustServerCertificate=True`**: justified only because SQL Server is on the same machine and traffic stays on loopback. With RDS, validate the certificate instead.
13. **Synthetic bulk data has unrealistic history** (only a "created" row), which is fine for plan comparisons only.
14. **Builds run on the production server**, so it needs the SDK and Node, and the build competes with the app for memory. A CI pipeline (e.g. GitHub Actions) building an artifact and copying it over would be cleaner.
15. **Rollback covers code, not schema.** Migrations have to be backward-compatible; there are no down-migrations in the deploy flow.
16. **No HTTPS without a domain name.** On a bare IP, passwords and the cookie cross the network unencrypted.
17. **Single server:** no load balancer, no redundancy. Backups are a documented manual command, not scheduled.
18. **No end-to-end tests in the repo.** The UI was clicked through with a scripted browser against a mock API during development, but that script isn't part of the project. Angular unit tests cover the interceptor, return-URL validation and error parsing only.
19. **The email hasn't been checked in real mail clients yet**, only in Chromium at desktop and phone widths. No dark-mode tuning.
20. **The triage feature sends client ticket text to a third-party API.** A real deployment would need that covered by the privacy policy, or kept disabled.
21. **One seeded agent and no user management UI.** Adding agents means another seed or a SQL insert with a hash.
