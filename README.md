# AcxiomCRM Enterprise Full-Stack Web Application

**AcxiomCRM** is an enterprise-grade, role-based Customer Relationship Management (CRM) application engineered in accordance with the official **AcxiomCRM Project Specification & Functional Documentation**. It covers the complete customer-sales lifecycle from prospect lead capture to opportunity pipeline management, follow-up touchpoint tracking, audit logging, and RESTful API data services.

---

## 1. Architectural Architecture & Stack

The application adheres to a clean layered enterprise architecture:
- **Presentation Layer**: Responsive Bootstrap 5 layout with intuitive sidebar navigation, toast notifications, client-side validation, and interactive **Chart.js 4.4** data visualizations.
- **Application / Controller Layer**: Strongly-typed ASP.NET Core 8.0 MVC Controllers, DTO projections, ViewModels, and REST API controllers with standard HTTP status conventions (`200 OK`, `201 Created`, `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`, `409 Conflict`).
- **Domain & Business Layer**: Strict server-side validation, duplicate prevention, lead-to-customer conversion orchestration, weighted pipeline calculation (`Amount × Probability / 100`), and date timeline constraints.
- **Data Access & Database Layer**: Entity Framework Core 8.0 with SQLite relational storage, parameterized LINQ queries (SQL Injection immunity), foreign-key referential integrity, and cascading delete management.
- **Security & Identity Layer**: Framework-managed **ASP.NET Core Identity** password hashing (no plain text storage), account lockout after 5 consecutive failed attempts for 15 minutes, role-based authorization (`Admin`, `Manager`, `SalesExecutive`), anti-forgery token verification (`[ValidateAntiForgeryToken]`), and an append-only audit trail.

---

## 2. Default Seed Test Accounts

The system automatically initializes and seeds the database with compliant users, customers, leads, opportunities, follow-ups, and audit logs on first boot:

| Role | Username / Email | Password | Access Scope |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin@acxiomcrm.com` | `Admin@12345` | Full system administration, user management, lockouts, audit logs, all records & reports |
| **Manager** | `manager@acxiomcrm.com` | `Manager@12345` | Team pipeline, opportunity forecasting, follow-ups, management reports |
| **Sales Executive** | `sales1@acxiomcrm.com` | `Sales@12345` | Assigned customers, assigned leads, assigned deals, planned follow-ups |
| **Sales Executive** | `sales2@acxiomcrm.com` | `Sales@12345` | Assigned records for multi-rep demonstration |

> **Quick Login Tip**: The login screen features **1-Click Test User Buttons** to instantly populate credentials for any role.

---

## 3. Mandatory Module Breakdown

### 3.1 Authentication & Security (`/Account`)
- **Login / Logout**: Form authentication with session cookies and return-URL navigation.
- **Account Lockout**: 5 failed login attempts trigger a 15-minute lockout screen and log a warning in `AuditLogs`.
- **Password Complexity Policy**: Minimum 8 characters with at least one uppercase letter, one lowercase letter, one digit, and one non-alphanumeric special character.

### 3.2 Dashboard (`/Dashboard`)
- **8 Executive KPI Cards**: Total Customers, Total Leads, Open Leads, Total Opportunities, Open Opportunities, Won Opportunities, Lost Opportunities, Total Pipeline Value, Weighted Forecast.
- **Date Filter Bar**: `Today`, `This Week`, `This Month`, and `All Time`.
- **3 Dynamic Chart.js Graphs**:
  1. **Lead Status Breakdown**: Doughnut chart (`New`, `Contacted`, `Qualified`, `Unqualified`, `Converted`, `Lost`).
  2. **Opportunity Pipeline**: Bar chart showing dollar volume per stage (`Qualification`, `Proposal`, `Negotiation`, `Won`, `Lost`).
  3. **Monthly Sales Revenue**: Line chart displaying sales closed trends over the last 6 months.

### 3.3 Customer Management (`/Customers`)
- Comprehensive CRUD operations, search across (Customer Name, Email, Phone, Company), status filter, and pagination.
- **Business Validations**: Unique email constraint, unique 10-digit phone constraint, duplicate customer prevention.
- Detailed profile showing linked opportunities, follow-up history, and activity timeline.

### 3.4 Lead Management (`/Leads`)
- Lead capture with source tracking (`Website`, `Referral`, `Cold Call`, `Advertisement`, `Partner`), priority ranking, and expected deal values.
- **Lead-to-Customer Conversion**: 1-click wizard converting qualified leads into both a Customer account and a Sales Opportunity while updating the lead status to `Converted`.

### 3.5 Opportunity Management (`/Opportunities`)
- Sales pipeline deal tracking across stages (`Qualification`, `Proposal`, `Negotiation`, `Won`, `Lost`).
- **Enforced Business Validations**:
  - `Amount > 0`: Rejects 0 or negative values.
  - `0 <= Probability <= 100`: Rejects percentages outside bounds.
  - `ExpectedCloseDate >= Today`: Rejects past closing targets for active opportunities.
  - Automatic computation of **Weighted Pipeline** = `Amount * Probability / 100`.

### 3.6 Follow-Up Management (`/FollowUps`)
- Scheduling touchpoints against Customers, Leads, or Deals.
- Types: `Call`, `Meeting`, `Email`, `Demo`, `Visit`.
- Validation: Scheduled date cannot be earlier than today for planned activities.
- 1-click "Mark as Completed" with completion remarks that automatically sync into the interaction activity history.

### 3.7 Activity Management (`/Activities`)
- Logging Calls, Meetings, Emails, and Tasks with timestamps and participants.

### 3.8 User & Role Administration (`/Users`) — *Admin Only*
- User provisioning, role assignment, account activation / deactivation toggles, lockout unlocking, and administrator password resets.

### 3.9 Audit Trail (`/AuditLogs`) — *Admin & Manager*
- Append-only immutable log capturing timestamp, acting user ID, module name, action (`Login`, `Failed Login`, `Logout`, `Create`, `Update`, `Delete`, `Convert`, `Security`), record ID, result, and IP address.

### 3.10 Reports & CSV Export (`/Reports`)
- Hub with direct summary metrics, pipeline breakdown, conversion funnel, and **One-Click CSV Export** for:
  - Customer Master Report (`/Reports/ExportCsv?type=customer`)
  - Lead Qualification Report (`/Reports/ExportCsv?type=lead`)
  - Opportunity Pipeline Report (`/Reports/ExportCsv?type=opportunity`)
  - Follow-Up Activity Report (`/Reports/ExportCsv?type=followup`)
  - Audit Trail Compliance Report (`/Reports/ExportCsv?type=audit`)

---

## 4. REST API Specification

All endpoints communicate via JSON using clean DTO models without leaking internal database schemas or passwords:

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/customers` | Search & list customer accounts |
| `GET` | `/api/customers/{id}` | Retrieve individual customer by ID |
| `POST` | `/api/customers` | Create new customer (enforces email/phone uniqueness) |
| `PUT` | `/api/customers/{id}` | Update customer record |
| `DELETE` | `/api/customers/{id}` | Delete customer record |
| `GET` | `/api/leads` | Search & list prospective leads |
| `POST` | `/api/leads` | Create new prospect lead |
| `GET` | `/api/opportunities` | List sales opportunities |
| `POST` | `/api/opportunities` | Create opportunity deal (validates amount > 0, probability 0-100) |
| `GET` | `/api/followups` | Retrieve follow-up tasks |
| `POST` | `/api/followups` | Schedule new follow-up (validates date >= today) |
| `GET` | `/api/reports/pipeline` | Aggregate open pipeline volume and stage distribution |

---

## 5. How to Run & Test

### Running the Application:
Open PowerShell in the project directory:
```powershell
cd "C:\Users\grees\.gemini\antigravity\scratch\AcxiomCRM"
dotnet run --urls "http://127.0.0.1:5000"
```
Then navigate to: **`http://127.0.0.1:5000`** in your browser.

### Running the Acceptance Test Suite:
To execute the automated test verification suite matching Section 17.19 of the documentation:
```powershell
powershell -ExecutionPolicy Bypass -File .\verify_crm.ps1
```
Result: **100% Pass** on all 14 mandatory acceptance scenarios.
