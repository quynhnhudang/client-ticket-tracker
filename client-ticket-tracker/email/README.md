# The escalation email

When an agent escalates a ticket, `EmailEscalationNotifier` sends this to engineering.

| File | What it is |
|---|---|
| `escalation-sample.html` | **The exact HTML the app sends**, rendered with sample data. Open it in a browser, or send it (below). |
| `escalation-sample.txt` | The plain-text part sent alongside it. |
| `escalation-sample.eml` | A complete MIME message (HTML + text). Double-click to open in **Outlook** or **Apple Mail**. |
| `send-sample.sh` | Sends the `.eml` through any SMTP server, to test real inboxes such as **Gmail**. |
| `../api/src/TicketTracker.Api/Templates/escalation.html` | The source template, with `{{Placeholders}}` and `NOTE:` comments explaining each technique. |

The sample was produced by the real `EscalationEmailBuilder` (the same code path the app uses), so what you test is what gets sent.

## How to test rendering

1. **Browser:** open `escalation-sample.html`. This is a quick check only; browsers are far more forgiving than mail clients.
2. **Outlook / Apple Mail:** open `escalation-sample.eml`. Outlook for Windows is the one that matters most: it renders HTML email with Microsoft Word's engine.
3. **Gmail, and all three together:** send it.
   - Sign up for [Mailtrap](https://mailtrap.io) and open *Email Sandbox → your inbox → SMTP settings*. Its HTML Check tab reports which CSS each client supports.
   - `SMTP_URL=smtp://<host>:<port> SMTP_USER=... SMTP_PASS=... FROM=... TO=... ./email/send-sample.sh`
   - To reach a real Gmail/Outlook.com inbox you need a sending provider rather than a sandbox. SendGrid replaced its free plan with a 60-day trial in March 2025; Mailtrap's sending service is an alternative.
4. **From the app:** set the `Email:*` settings (see `docs/LOCAL-SETUP.md`, step 11) and escalate a ticket.

What to check: the header bars and badge colours, the two-column summary, the red left border on "Why it was escalated", the button (full-size and rounded in Outlook for Windows via VML), line breaks in the note and description, and the layout at phone width.

## Where the template avoids modern CSS, and why

| Web-page technique | What the email does instead | Why |
|---|---|---|
| `<style>` blocks, classes, `<link>` stylesheets | Every rule is an inline `style="..."` attribute | Some clients and security gateways strip `<head>` styles; inline styles survive everywhere. |
| Flexbox / CSS grid for layout | Nested `<table role="presentation">` with `width` attributes | Outlook for Windows supports neither; tables are the only layout that renders identically in every client. |
| `max-width` for a centred column | `max-width:600px` **plus** an Outlook-only "ghost table" fixed at 600px (`<!--[if mso]>`) | Outlook ignores `max-width`. Other clients get a fluid column that shrinks on phones. |
| Media queries for mobile | Fluid widths (`width:100%` + `max-width`) | Media queries need a `<style>` block. |
| Padding on an `<a>` button | VML `<v:roundrect>` for Outlook; padded `<a>` for everyone else | Outlook ignores padding on inline elements, so the button would shrink to its text. |
| `background-color` alone | `bgcolor` attribute **and** `background-color` | Outlook honours the HTML attribute more reliably. |
| `rgba()`, CSS variables, gradients | 6-digit hex colours | Not supported in Outlook for Windows. |
| `::before`, `box-shadow` for the note's accent bar | `border-left` on a table cell | Pseudo-elements and shadows are widely unsupported; cell borders work everywhere. |
| Web fonts (`@font-face`) | `Arial, Helvetica, sans-serif` | Web fonts need `<style>`; unknown fonts fall back to Times New Roman in Outlook. |
| Default margins | Explicit `margin:0` on headings and `body` | Default heading and body margins differ from client to client. |
| `display:none` for hidden preview text | `display:none` + `max-height:0` + `opacity:0` + `mso-hide:all` | No single property hides it in every client. |

## Security choices
- Every value from the database is **HTML-encoded** (`WebUtility.HtmlEncode`). A client could type `<a href="https://phish.example">Reset your password</a>` into a description; encoded, it shows up as text instead of a working link inside an official-looking email.
- Newlines become `<br>` **after** encoding, so the only tags in the email are the template's own.
- Placeholders are replaced in one pass, so a description containing `{{TicketUrl}}` stays literal text.
- **Line breaks are stripped from the subject** (it's built from client-typed text), which blocks header injection.
- Author comments are stripped before sending. The Outlook conditional comments are kept, because they do real work.

## Known limitations (disclose if asked)
- There is no dark-mode tuning. Apple Mail and Outlook.com may invert the colours automatically.
- I checked it only in Chromium, at desktop and phone widths. Your Gmail, Outlook and Apple Mail tests are the real verification.
- Sending happens once, right after the escalation commits. A failure is logged, not retried (see `docs/DECISIONS.md`, "outbox").
