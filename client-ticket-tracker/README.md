# Client Ticket Tracker

A support-ticket system that follows the real support-engineering loop:
**intake → triage → escalate to engineering → resolve**.

| Layer | Technology |
|---|---|
| Frontend | Angular 22 (standalone components, signals, lazy routes) |
| API | C# / ASP.NET Core Web API on .NET 10, EF Core 10 |
| Database | SQL Server 2022/2025; full-text search, with an automatic fallback when it's not installed |
| Search | Hand-written, parameterised ADO.NET query (`Data/Queries/TicketSearchQuery.cs`) |
| Email | Table-based, inline-CSS HTML email over SMTP (MailKit) |
| Hosting | AWS EC2 (Ubuntu 24.04), nginx reverse proxy, Kestrel under systemd, Bash deploy script |
| Optional | LLM triage suggestion (Claude API), suggested but never auto-applied |

## Features
- **Intake:** title, description, category (bug / question / data fix), priority, and client (matched by email)
- **Queue:** filter by status and priority; most urgent and oldest first; the filters live in the URL
- **Ticket detail:** description, resolution notes, internal comments, and a full status-history timeline
- **Workflow:** Open → In progress → Escalated → Resolved (plus reopen), enforced server-side; the UI only offers valid moves
- **Escalation:** requires a note, and emails engineering an HTML summary with a link back to the ticket
- **Search:** `#123`, a client-name prefix, or keywords (full-text), from one box
- **Auth:** agent login with an HttpOnly, SameSite=Strict cookie; rate-limited

## Repository layout

```
api/
  src/TicketTracker.Api/
    Domain/            entities, enums, TicketStatusRules (the lifecycle)
    Data/              AppDbContext (schema + indexes), DbSeeder
    Data/Queries/      TicketSearchQuery: the hand-written SQL
    Services/          TicketService (every write goes through it)
    Services/Email/    EscalationEmailBuilder, SmtpEmailSender (MailKit), notifier
    Services/Triage/   optional LLM suggestion + output validation
    Templates/         escalation.html (the email template)
    Controllers/       Auth, Tickets, Triage, Health
  tests/               xUnit: status rules, search input, email builder, triage parser
web/                   Angular app (core/ services, features/ pages, shared/ badge)
db/                    full-text setup, sample data, 50k-row data, query-plan comparisons, Docker SQL Server
email/                 rendered sample email (.html / .txt / .eml) + send script
deploy/                DEPLOY.md, deploy.sh, nginx site, systemd unit, SQL logins, env templates
docs/
  LOCAL-SETUP.md       run and test everything on your machine, step by step
  DECISIONS.md         every design decision, plus known gaps to disclose
```

## Getting started
1. **Local:** [docs/LOCAL-SETUP.md](docs/LOCAL-SETUP.md)
2. **Email rendering tests:** [email/README.md](email/README.md)
3. **Deploy to EC2:** [deploy/DEPLOY.md](deploy/DEPLOY.md)

## API

| Method | Route | Notes |
|---|---|---|
| GET | `/api/health` | anonymous; 503 if the DB is unreachable |
| POST | `/api/auth/login` | sets the HttpOnly cookie; 5 attempts/min per IP |
| POST | `/api/auth/logout` | |
| GET | `/api/auth/me` | 401 if not logged in |
| GET | `/api/tickets?status=&priority=&page=&pageSize=` | the queue |
| GET | `/api/tickets/search?q=` | `#123`, client-name prefix, or keywords |
| GET | `/api/tickets/{id}` | detail + comments + history + allowed next statuses |
| POST | `/api/tickets` | intake |
| POST | `/api/tickets/{id}/status` | the only way to change status; escalation triggers the email |
| POST | `/api/tickets/{id}/comments` | internal comment |
| GET | `/api/triage/status` | is the LLM suggestion configured? |
| POST | `/api/triage/suggest` | category + priority suggestion; 20/min per agent |
