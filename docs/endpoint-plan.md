# RaceDay - API Endpoint Plan (Part 1, Section B)

This table is the specification for the Part 2 API. Every row below is implemented
by a controller action in `src/RaceDay.Api/Controllers`, and every route is visible in Swagger UI.

**Conventions**

- Base path: `/api`. All bodies are JSON.
- **Authentication = server-issued session cookie** (`RaceDay.Session`). `POST /api/auth/login` creates it;
  the browser / HTTP client then sends it automatically on every request. Role (`Organiser` or `Participant`) is stored in the session identity.
- "Role Required" values:
  - **Public** - no login needed
  - **Any user** - logged in, either role
  - **Organiser** / **Participant** - logged in with that role only
  - **Organiser (owner)** - Organiser who created the event in question
  - **Participant (owner)** - Participant who made the enrolment in question
- Standard error responses (all return a JSON problem body):
  `400` validation failed - `401` not logged in - `403` wrong role or not the owner - `404` not found - `409` conflict (duplicate / still in use).

## 1. Authentication

| # | HTTP Method | Route | Description | Role Required | Request Body | Expected Response |
|---|---|---|---|---|---|---|
| 1 | POST | `/api/auth/register` | Create an account. User supplies personal details and chooses a role (`Organiser` or `Participant`). Password is hashed (BCrypt) before storage. | Public | `{ firstName, lastName, email, password, role, phoneNumber?, dateOfBirth? }` | `201 Created` + user (no password). `400` invalid data / unknown role / password < 8 chars. `409` email already registered. |
| 2 | POST | `/api/auth/login` | Verify credentials and start a session (sets cookie). | Public | `{ email, password }` | `200 OK` + user (`userId`, `firstName`, `lastName`, `email`, `role`, ...). `401` invalid credentials. |
| 3 | POST | `/api/auth/logout` | End the current session. | Any user | none | `204 No Content`. `401` if not logged in. |

## 2. User Profile

| # | HTTP Method | Route | Description | Role Required | Request Body | Expected Response |
|---|---|---|---|---|---|---|
| 4 | GET | `/api/profile` | View my own profile. | Any user | none | `200 OK` + user. `401`. |
| 5 | PUT | `/api/profile` | Update my own profile (name, phone, date of birth). Email and role cannot be changed. | Any user | `{ firstName, lastName, phoneNumber?, dateOfBirth? }` | `200 OK` + updated user. `400`. `401`. |

## 3. Events

| # | HTTP Method | Route | Description | Role Required | Request Body | Expected Response |
|---|---|---|---|---|---|---|
| 6 | GET | `/api/events` | Browse all events, soonest first. Optional filter `?eventType=Run\|Walk\|Cycle`. | Any user | none | `200 OK` + list of events. `400` bad filter. `401`. |
| 7 | GET | `/api/events/{id}` | View one event. | Any user | none | `200 OK` + event. `401`. `404`. |
| 8 | GET | `/api/events/mine` | List events created by the logged-in Organiser. | Organiser | none | `200 OK` + list of events. `401`. `403`. |
| 9 | POST | `/api/events` | Create an event. Organiser becomes its owner. | Organiser | `{ name, description, eventDate, location, distanceKm, eventType }` | `201 Created` + event. `400`. `401`. `403`. |
| 10 | PUT | `/api/events/{id}` | Update an event. | Organiser (owner) | `{ name, description, eventDate, location, distanceKm, eventType }` | `200 OK` + event. `400`. `401`. `403` (participant, or another organiser's event). `404`. |
| 11 | DELETE | `/api/events/{id}` | Delete an event together with its categories, enrolments and results. | Organiser (owner) | none | `204 No Content`. `401`. `403`. `404`. |

## 4. Categories

| # | HTTP Method | Route | Description | Role Required | Request Body | Expected Response |
|---|---|---|---|---|---|---|
| 12 | GET | `/api/events/{eventId}/categories` | View the categories offered by an event. | Any user | none | `200 OK` + list of categories. `401`. `404` event. |
| 13 | POST | `/api/events/{eventId}/categories` | Add an age or distance category to an event (e.g. "Under 20", "21km"). | Organiser (owner) | `{ name, minAge?, maxAge?, distanceKm? }` | `201 Created` + category. `400`. `401`. `403`. `404`. `409` duplicate name for this event. |
| 14 | PUT | `/api/events/{eventId}/categories/{categoryId}` | Update a category. | Organiser (owner) | `{ name, minAge?, maxAge?, distanceKm? }` | `200 OK` + category. `400`. `401`. `403`. `404`. `409`. |
| 15 | DELETE | `/api/events/{eventId}/categories/{categoryId}` | Remove a category (only if nobody has enrolled in it). | Organiser (owner) | none | `204 No Content`. `401`. `403`. `404`. `409` category has enrolments. |

## 5. Event Enrolments

| # | HTTP Method | Route | Description | Role Required | Request Body | Expected Response |
|---|---|---|---|---|---|---|
| 16 | POST | `/api/enrolments` | Enter an event by choosing one of its categories. Records participant + event + category. | Participant | `{ eventId, categoryId }` | `201 Created` + enrolment. `400` category does not belong to the event. `401`. `403` (organisers cannot enrol). `404` event/category. `409` already enrolled in this event. |
| 17 | GET | `/api/enrolments/mine` | View my own enrolments. | Participant | none | `200 OK` + list of enrolments. `401`. `403`. |
| 18 | DELETE | `/api/enrolments/{id}` | Withdraw from an event (my own enrolment only; not once a result exists). | Participant (owner) | none | `204 No Content`. `401`. `403`. `404`. `409` a result is already recorded. |
| 19 | GET | `/api/events/{eventId}/enrolments` | View all enrolments for one of my events. | Organiser (owner) | none | `200 OK` + list of enrolments. `401`. `403`. `404`. |

## 6. Results

| # | HTTP Method | Route | Description | Role Required | Request Body | Expected Response |
|---|---|---|---|---|---|---|
| 20 | POST | `/api/events/{eventId}/results` | Capture a finish time and finishing position for an enrolled participant. | Organiser (owner) | `{ enrolmentId, finishTime ("hh:mm:ss"), position }` | `201 Created` + result. `400` (enrolment not in this event / invalid values). `401`. `403`. `404`. `409` result already exists for the enrolment, or position already taken. |
| 21 | PUT | `/api/events/{eventId}/results/{resultId}` | Correct a captured result. | Organiser (owner) | `{ finishTime, position }` | `200 OK` + result. `400`. `401`. `403`. `404`. `409` position already taken. |
| 22 | GET | `/api/events/{eventId}/results` | View all results for one of my events, ordered by position. | Organiser (owner) | none | `200 OK` + list of results. `401`. `403`. `404`. |
| 23 | GET | `/api/results/mine` | View my own results across all events. | Participant | none | `200 OK` + list of results. `401`. `403`. |

## Access summary

| Capability | Organiser | Participant | Not logged in |
|---|:-:|:-:|:-:|
| Register / log in | yes | yes | yes |
| View / update own profile | yes | yes | no |
| Browse events and categories | yes | yes | no |
| Create / edit / delete events and categories | yes (own events) | no | no |
| Enrol in an event | no | yes | no |
| View own enrolments / withdraw | no | yes | no |
| View all enrolments for an event | yes (own events) | no | no |
| Capture / correct / view event results | yes (own events) | no | no |
| View own results | no | yes | no |

## Design decisions

- **Role at registration:** the role is validated against the `Roles` table (only `Organiser` and `Participant` exist), so nobody can register with an arbitrary role. The role is stored on the user and copied into the session identity at login.
- **Ownership:** an Organiser may only change events (and their categories, enrolments view and results) that they created; this is a `403` otherwise.
- **Passwords:** stored only as BCrypt hashes; never returned by any endpoint.
- **Single result per enrolment, unique position per event** are enforced by the API (`409`); one result per enrolment is also a database unique index.
