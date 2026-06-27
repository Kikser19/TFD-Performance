# Fitness Coaching Platform — Architecture Guide (V1)

This document is the build spec for the project. Read it fully before writing any code.
It defines scope, service boundaries, data ownership, communication patterns, and build
order. Do not add features, services, or infrastructure beyond what's described here
without flagging it first — this is a deliberately small V1, not the final system.

---

## 1. What this product is

A platform a personal trainer uses to sell and deliver two kinds of programs:

1. **Online program** — client pays, gets instant access, follows a pre-built workout
   program on their own, logs their own sets/reps and daily nutrition.
2. **1:1 coaching** — trainer manually builds and assigns programs to a specific client
   (no self-checkout), tracks that client's progress, can send a workout for a day they
   can't train together in person.

Both flows share the same underlying engine: a **program** is made of **workouts**, a
workout is made of **exercises**, and a client logs **actual performance** against what
was prescribed. The only real differences between "online" and "1:1" are (a) whether a
payment gates access, and (b) who assigns the program. Do not build these as two separate
systems.

All exercise demo videos are hosted as **unlisted YouTube videos**. The app stores the
YouTube video ID per exercise and embeds it via the YouTube IFrame API / oEmbed — the app
never hosts or proxies video files itself.

---

## 2. V1 scope — explicitly in and out

**In scope for V1:**
- Trainer and client accounts, login
- Trainer creates exercises (with YouTube video ID), workouts, and programs
- Trainer assigns a program to a specific client (1:1 flow)
- Online programs: listed, purchasable, instant access on payment
- Client logs sets/reps/weight per exercise per workout session
- Client logs daily nutrition entries (manual entry: food name, calories, macros)
- Email notifications: purchase confirmation, "your trainer assigned you a program"
- Trainer dashboard: see client list, see a client's recent logs

**Explicitly out of scope for V1** — do not build, do not scaffold, do not leave
placeholder code for these. They come later as separate, additive work:
- In-app chat
- AI-based analysis of logs/nutrition
- Third-party calorie/food database API integration (manual entry only for now)
- Push notifications (email only for V1)
- Kubernetes/Helm/ArgoCD deployment (V1 deploys via docker-compose only — see §7)
- Mobile native app (responsive web app only)
- Multi-trainer / multi-tenant support (one trainer's business, for now)

---

## 3. Architecture style

This is a **microservices** system, built deliberately small: six services total for V1,
not one more. Each service:
- Owns its own Postgres database. No service is ever allowed to query another
  service's database directly, even read-only. If it needs data it doesn't own, it asks
  over the network (sync call) or reacts to an event (async).
- Is its own ASP.NET Core project, with its own Dockerfile.
- Can be built, tested, and (in theory) deployed independently of the others.

**Services:**

| Service | Owns | Responsible for |
|---|---|---|
| Identity Service | Users table | Signup, login, JWT issuance, roles (trainer/client) |
| Program Service | Programs, Workouts, Exercises, Assignments tables | Creating/editing programs, workouts, exercises; assigning programs to clients; access control (does this client have access to this program) |
| Tracking Service | Logs, NutritionEntries tables | Recording set/rep/weight logs, recording nutrition entries, reading back a client's history |
| Billing Service | Purchases table | Stripe checkout for online programs, recording purchase status |
| Notification Service | (no DB needed for V1 — stateless) | Sending emails in reaction to events |
| API Gateway | nothing — no DB | Single entry point the frontend talks to; routes requests to the right service; validates JWTs before forwarding |

**Frontend:** one React app, talks only to the API Gateway, never directly to any
individual service.

---

## 4. Communication patterns

Two patterns are used, and the choice is not arbitrary — pick the right one per case.

**Synchronous (direct REST call):** use when the caller needs an answer *right now*
before it can proceed.
- Gateway → Identity: "is this JWT valid, what role does this user have"
- Gateway → Program Service: "does this client have access to this program" (before
  letting a workout be fetched)

**Asynchronous (event via RabbitMQ):** use when something happened, and other services
need to react, but the original action must not wait on them.
- Billing Service publishes `ProgramPurchased` after a successful Stripe payment.
  Program Service consumes it (grants access / creates an Assignment row). Notification
  Service consumes it (sends the confirmation email). Billing does not call either of
  them directly and does not wait for them to finish.
- Program Service publishes `ProgramAssigned` when a trainer assigns a program to a
  client. Notification Service consumes it (sends "your trainer assigned you a program").

**Implementation:** use **MassTransit** as the abstraction over **RabbitMQ**. Define
event contracts as shared C# record types (see §6) so producers and consumers agree on
shape without copy-pasting class definitions across services.

**Rule of thumb to apply when unsure:** if the user is sitting there waiting for the
result on screen, it's sync. If it's "and also notify/update some other part of the
system," it's an event.

---

## 5. Data model per service

Each service's database is independent. Foreign-key-style references *across* services
(e.g. Program Service storing a `clientId`) are just plain UUIDs with no actual foreign
key constraint — the referenced row lives in a different database entirely, so the
database engine can't enforce it. Integrity across services is maintained by the events
in §4, not by SQL constraints.

### Identity Service
- `Users`: id (PK), name, email (unique), password_hash, role (trainer | client), created_at

### Program Service
- `Exercises`: id (PK), trainer_id, name, youtube_video_id, instructions
- `Workouts`: id (PK), program_id, title, order_index
- `WorkoutExercises`: id (PK), workout_id, exercise_id, prescribed_sets, prescribed_reps, prescribed_rest_seconds, order_index
- `Programs`: id (PK), trainer_id, title, type (online | one_on_one), price_cents (nullable for one_on_one), status (draft | published)
- `Assignments`: id (PK), program_id, client_id, granted_at, source (purchase | manual)

### Tracking Service
- `WorkoutLogs`: id (PK), client_id, workout_id, performed_at
- `SetLogs`: id (PK), workout_log_id, workout_exercise_id, set_number, reps_completed, weight_used
- `NutritionEntries`: id (PK), client_id, logged_at, food_name, calories, protein_g, carbs_g, fat_g

### Billing Service
- `Purchases`: id (PK), client_id, program_id, stripe_payment_id, amount_cents, status (pending | completed | failed), created_at

### Notification Service
No persistent storage needed for V1 — it's a pure event consumer that sends email and
forgets. (If you want send-history/auditing later, add a `SentNotifications` table then,
not now.)

---

## 6. Event contracts

Define these as shared C# records in a small shared library referenced by every service
that needs them (producers and consumers both reference it — this is the *only* code
shared between services; do not share business logic or database access code).

```csharp
public record ProgramPurchased(Guid ClientId, Guid ProgramId, Guid PurchaseId, DateTime PurchasedAt);

public record ProgramAssigned(Guid ClientId, Guid ProgramId, Guid AssignedByTrainerId, DateTime AssignedAt);

public record UserRegistered(Guid UserId, string Email, string Name, string Role, DateTime RegisteredAt);
```

Keep event payloads small and flat — just enough for the consumer to act or to know what
to fetch. Don't embed entire related objects in an event; if Notification Service needs
the client's email to send a message, it should already know it (from `UserRegistered`)
or fetch it from Identity Service directly rather than have every event carry a copy of
user data everywhere.

---

## 7. Tech stack

- **Backend:** ASP.NET Core (minimal APIs), one project per service
- **Database:** PostgreSQL, one database per service, EF Core for migrations/access
- **Messaging:** RabbitMQ + MassTransit
- **Gateway:** YARP (Yet Another Reverse Proxy) — routes by path prefix, validates JWT
  before forwarding, strips it and forwards a trusted internal header instead
- **Auth:** JWT issued by Identity Service, validated at the Gateway
- **Frontend:** React + TypeScript, talks only to the Gateway
- **Payments:** Stripe Checkout (hosted payment page, not a custom card form)
- **Video:** YouTube IFrame API, videos unlisted, video ID stored per exercise
- **Email:** any transactional email provider with a simple HTTP API (e.g. Resend,
  Postmark) — wrap it behind a small interface in Notification Service so the provider
  can be swapped later without touching other services

---

## 8. Local development & deployment (V1)

**No Kubernetes for V1.** Everything runs via a single `docker-compose.yml` at the repo
root:

- One container per service (6 backend containers + frontend)
- One Postgres container per service that needs one (4 databases: identity, program,
  tracking, billing) — either 4 separate Postgres containers, or one Postgres container
  with 4 separate databases inside it. Either is fine for V1; separate containers is
  closer to the eventual "fully independent" target if you want the practice.
- One RabbitMQ container (with the management plugin enabled, so you get a web UI at
  `:15672` to see queues and messages — useful for learning/debugging)
- Each service's Dockerfile should be production-shaped from day one (multi-stage build,
  non-root user) so the *same* image can later become a Helm chart without rewriting it —
  only the deployment manifest changes when you eventually move to your K8s cluster.

**Environment config:** every service reads its config (DB connection string, RabbitMQ
host, JWT signing key) from environment variables, never hardcoded — docker-compose
injects these per-container.

---

## 9. Repository structure

Monorepo. Suggested layout:

```
/services
  /identity-service
    /src
    Dockerfile
  /program-service
    /src
    Dockerfile
  /tracking-service
    /src
    Dockerfile
  /billing-service
    /src
    Dockerfile
  /notification-service
    /src
    Dockerfile
  /gateway
    /src
    Dockerfile
/shared
  /events            <- the shared event-contract library from §6
/frontend
  /src
/docker-compose.yml
/docs
  architecture-guide.md   <- this file
```

---

## 10. API conventions

- REST, JSON, plural nouns: `/api/programs`, `/api/programs/{id}/workouts`
- Auth: `Authorization: Bearer <jwt>` on every request through the Gateway
- The Gateway is the only thing the frontend's base URL ever points at — individual
  service ports are never exposed to the frontend, even in local dev
- Standard HTTP status codes; error responses are `{ "error": "human-readable message" }`
- Pagination on any list endpoint that could grow unbounded (client logs, nutrition
  entries): `?page=1&pageSize=20`

---

## 11. Build order

Build and get each step actually running (not just compiling) before moving to the next.
Don't parallelize this for V1 — each step de-risks the next one.

1. **Identity Service** — signup, login, JWT issuance. Test with raw HTTP calls (curl/Postman) before anything else exists.
2. **API Gateway** — routes to Identity, validates JWTs. Confirms the auth flow end-to-end.
3. **Program Service** — exercises, workouts, programs, manual assignment (skip purchase flow for now — a trainer can assign a program to a client with no payment involved). This is the biggest service; take your time on it.
4. **Tracking Service** — logging sets/reps and nutrition against an assigned program.
5. **Frontend** — build against the three services above. At this point you have a fully working 1:1 coaching flow with no payments involved yet. This is a legitimate, demoable milestone.
6. **RabbitMQ + MassTransit wiring** — introduce the event bus now, retrofitting Program Service to publish `ProgramAssigned` and Notification Service to consume it. Do this before Billing so you learn the messaging pattern on the simpler event first.
7. **Billing Service** — Stripe Checkout integration, publishes `ProgramPurchased`, Program Service consumes it to create an Assignment automatically. This completes the online-program flow.
8. **Notification Service: purchase confirmation email** — consumes `ProgramPurchased`.

Each step has a clear "done" condition: you can do the entire user-facing action through
the running system, not just "the code exists."

---

## 12. Conventions for whoever (human or AI) builds this

- Each service is independently runnable — `docker-compose up identity-service` plus its
  DB and nothing else should work for local iteration on that service alone.
- No service references another service's EF Core `DbContext` or connection string,
  ever, under any circumstance.
- Shared code is limited strictly to the event-contract library in `/shared/events`.
  Resist the urge to add a shared "common utils" project — it's the first step toward
  services becoming coupled again.
- Commit messages and PRs should be scoped to one service at a time where possible —
  this is part of the discipline that makes the architecture real rather than cosmetic.
- When in doubt about whether something is a new service or belongs inside an existing
  one: default to putting it inside the closest existing service. Splitting a service
  later is easy; merging two badly-split services back together is not. Don't pre-split.
