# RaceDay-2
c# api ptoject

# RaceDay

Event management for South African running, walking and cycling events.
This repository contains **Part 1 (planning + database)** and **Part 2 (RESTful API)** of the RaceDay PoE.

| Part | Where |
|---|---|
| Part 1 - ERD | [`docs/ERD.png`](docs/ERD.png) (also `ERD.pdf`, source `ERD.dot`) |
| Part 1 - API endpoint plan | [`docs/endpoint-plan.md`](docs/endpoint-plan.md) |
| Part 1 - SQL script | [`docs/raceday.sql`](docs/raceday.sql) |
| Part 2 - API | [`src/RaceDay.Api`](src/RaceDay.Api) |
| Part 2 - Unit tests | [`tests/RaceDay.Api.Tests`](tests/RaceDay.Api.Tests) |
| Part 2 - CI/CD | [`.github/workflows/ci.yml`](.github/workflows/ci.yml) |

## Tech stack

ASP.NET Core 8 Web API - Entity Framework Core 8 (code-first, SQL Server) - cookie-based session authentication -
BCrypt password hashing - Swagger (Swashbuckle) - xUnit + `WebApplicationFactory` - GitHub Actions.

## Running the API

**Prerequisites:** Visual Studio 2022 (ASP.NET workload), .NET 8 SDK, SQL Server LocalDB (installed with Visual Studio) or any SQL Server.

1. Open `RaceDay.sln` in Visual Studio. (If it will not open, run `dotnet new sln -n RaceDay` then
   `dotnet sln add src/RaceDay.Api tests/RaceDay.Api.Tests` in this folder.)
2. Check the connection string in `src/RaceDay.Api/appsettings.json`. The default uses LocalDB and creates a database called `RaceDayApiDb`.
3. **Create the database from the code-first model.** In *Tools > NuGet Package Manager > Package Manager Console*
   (Default project: `RaceDay.Api`):

   ```powershell
   Add-Migration InitialCreate
   Update-Database
   ```

   or from a terminal in the repository root:

   ```bash
   dotnet tool install --global dotnet-ef        # once
   dotnet ef migrations add InitialCreate --project src/RaceDay.Api
   dotnet ef database update --project src/RaceDay.Api
   ```

   Commit the generated `Migrations` folder - it is your proof of the code-first approach.
4. Press **F5**. Swagger UI opens at `/swagger`.

### Trying it in Swagger

1. `POST /api/auth/register` - create an Organiser (`"role": "Organiser"`) and a Participant.
2. `POST /api/auth/login` - the browser stores the `RaceDay.Session` cookie, so every other endpoint on the page now works as that user.
3. Try `POST /api/events` as the Organiser, then `POST /api/auth/logout`, log in as the Participant and try `POST /api/enrolments`.
   Trying an Organiser endpoint as the Participant returns `403`; with no login it returns `401`.

### Using the Part 1 SQL script instead / as well

`docs/raceday.sql` creates a **separate** database called `RaceDayDb` with sample data (2 organisers, 3 participants, 4 events, categories, enrolments and results).
Every seeded user has the password `Password123!`.

- To explore the sample data through the API, run the script, then point `DefaultConnection` at `RaceDayDb` and **skip step 3** (the schema is the same).
- Do not run `Update-Database` against a database the script already created - the tables already exist.

## Authentication and roles

- `POST /api/auth/register` accepts personal details **and a role**. Only `Organiser` and `Participant` are accepted: the role is looked up in the `Roles` table, so nobody can register with an invented role.
- `POST /api/auth/login` verifies the password and starts a **server-issued session**: an HttpOnly cookie (`RaceDay.Session`, 30-minute sliding expiry) that carries the user's id and role on every later request. `POST /api/auth/logout` ends it.
- Passwords are stored only as **BCrypt hashes** (salted, work factor 11) and are never returned by any endpoint.
- Endpoints are protected with `[Authorize]` / `[Authorize(Roles = ...)]`: not logged in -> `401`, wrong role -> `403`.
- **Ownership:** an Organiser can only manage events they created (and those events' categories, enrolments and results) -> `403` otherwise. A Participant can only see and withdraw their own enrolments and see their own results.
- Because the session is a cookie, the Part 3 MVC front end can call the API with the same cookie, or share the same authentication approach. The cookie is `SameSite=Lax`, which blocks cross-site form posts.

## Tests

```bash
dotnet test
```

The tests start the real API in memory and replace SQL Server with an isolated in-memory database, so they need no setup and run identically on GitHub Actions. They cover:

| File | Covers |
|---|---|
| `AuthenticationTests` | register (both roles, bad role, duplicate email, weak password, password hashed in the DB), login (success + session cookie, wrong password, unknown email), logout, profile view/update |
| `EventManagementTests` | Organiser create / update / delete events and categories (success, validation failures, ownership `403`, `404`), both roles viewing events |
| `RoleAccessTests` | every Organiser-only endpoint rejects a Participant (`403`), every Participant-only endpoint rejects an Organiser (`403`), every protected endpoint rejects anonymous users (`401`) |
| `EnrolmentTests` | Participant enrols and the participant/event/category link is stored, duplicate enrolment, wrong-event category, unknown event/category, Organiser views enrolments, withdrawal rules |
| `ResultTests` | Organiser captures/corrects results, Participant sees only their own, duplicate result/position, invalid times, ownership |

## CI/CD

`.github/workflows/ci.yml` runs on every push and pull request to `main`/`master`: restore, build (Release), then `dotnet test`.

## Plan versus implementation

The implementation follows `docs/endpoint-plan.md` row for row (23 endpoints) and `docs/raceday.sql` / `docs/ERD.png` table for table. Deliberate points to be aware of:

- **`AppUser` class / `Users` table.** The C# class is called `AppUser` (a class called `User` clashes with `ControllerBase.User`); it maps to the `Users` table in the ERD.
- **`EventType` is a column, not a lookup table.** It stores `Run`, `Walk` or `Cycle` and is protected by a `CHECK` constraint.
- **Result positions.** One result per enrolment is a database unique index. "One position per event" cannot be expressed as a simple unique index, so the API enforces it (`409`).
- **Deleting an event** removes its categories, enrolments and results. `ON DELETE NO ACTION` is used for the Category -> Enrolment link to avoid SQL Server's "multiple cascade paths" error.
- **Seed roles.** The two rows in `Roles` are seeded by the EF model (`HasData`) so a fresh database can accept registrations.

## Git workflow reminder (Part 1 requirement)

The brief requires the planning documents to be committed to `/docs` **before** any API code. Commit in this order:

```bash
git init
git add docs README.md && git commit -m "Part 1: ERD, endpoint plan and SQL script"
git add . && git commit -m "Part 2: RESTful API, tests and CI"
```
