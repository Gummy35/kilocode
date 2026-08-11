# BUG-CLI-001 — Visual Studio Experimental Instance Cannot Connect to Kilo CLI

**Task:** BUG-CLI-001  
**Type:** Bug / Regression  
**Status:** NOT_STARTED  
**Priority:** HIGH  
**Scope:** `packages/kilo-visualstudio`  
**Date:** 2026-08-11

---

## 1. Problem Statement

The Kilo Visual Studio extension builds successfully, but when launched in a Visual Studio Experimental/Debug instance, it fails to establish a connection with the Kilo CLI server.

The expected behavior is:

```text
Visual Studio Experimental Instance
        ↓
Extension initialization
        ↓
CliBackendManager
        ↓
Kilo CLI process
        ↓
CLI listens on 127.0.0.1:<port>
        ↓
KiloConnectionService
        ↓
NSwag KiloApiClient
        ↓
Authenticated health request
        ↓
Connected state
        ↓
SSE connection
```

The actual behavior is that the extension does not reach the connected state.

This is a **runtime regression**. A successful compilation is therefore insufficient to consider this task complete.

The root cause is currently unknown.

Do not assume that NSwag is responsible. The regression may be in CLI startup, environment configuration, authentication, BaseUrl configuration, connection initialization, service lifecycle, or another change made during the Kiota → NSwag migration and cleanup.

---

## 2. Context

The Visual Studio extension recently underwent the following changes:

1. CLI/HTTP infrastructure was ported from the VS Code implementation.
2. A generated Kiota C# client was initially introduced.
3. The REST layer was subsequently migrated from Kiota to NSwag.
4. Handler services were migrated from Kiota to NSwag.
5. Kiota cleanup was performed.
6. The extension still builds successfully.

The intended final REST architecture is:

```text
KiloConnectionService
        │
        ├── KiloApiClient (NSwag)
        │       └── REST/OpenAPI
        │
        └── SseClient
                └── SSE /global/event
```

Kiota must not be reintroduced as a solution.

---

## 3. Known Constraints

### CLI path

The CLI executable path is intentionally hard-coded for now.

**Do not:**

- implement CLI discovery;
- search multiple locations;
- download the CLI;
- implement JetBrains-style CLI installation;
- change the existing CLI path strategy.

CLI discovery/downloading is explicitly out of scope.

### HTTP client

The intended REST client is NSwag.

Do not introduce another REST HTTP client.

Do not restore:

- Kiota;
- `HttpClientWrapper`;
- `CachedHttpClient`.

### Generated code

Do not manually modify generated NSwag code.

If the generated client is found to be incorrectly configured, fix the generation/configuration mechanism rather than patching generated output.

---

## 4. Investigation Requirements

Trace the complete connection lifecycle.

### 4.1 Extension startup

Verify:

- extension package initialization;
- service registration;
- creation of `CliBackendManager`;
- creation of `KiloConnectionService`;
- whether connection startup is actually triggered;
- whether startup ordering differs between normal VS and Experimental/Debug VS.

Determine whether the connection attempt happens at all.

---

### 4.2 CLI process startup

Inspect `CliBackendManager.cs`.

Verify:

- CLI executable path;
- working directory;
- process startup;
- command line arguments;
- `serve --port 0`;
- environment variables;
- stdout handling;
- stderr handling;
- process lifetime.

Confirm that the CLI process actually starts in the Experimental instance.

Determine whether it:

- remains alive;
- exits immediately;
- reports an error;
- reports a listening address.

---

### 4.3 Port discovery

Verify the complete port discovery mechanism.

Confirm that the extension receives the expected CLI output:

```text
listening on http://127.0.0.1:PORT
```

Verify:

- regex matching;
- stdout encoding;
- asynchronous output handling;
- extracted port;
- resulting BaseUrl.

The final BaseUrl must correspond to the actual CLI listening endpoint.

---

### 4.4 Environment variables

Compare the current environment setup against the known VS Code implementation and the previous working Visual Studio implementation.

Pay particular attention to:

- `KILO_SERVER_PASSWORD`
- `KILO_PARENT_PID`
- `KILO_CLIENT`
- `KILO_PLATFORM`
- `KILO_APP_NAME`
- `KILO_APP_VERSION`
- `KILOCODE_VERSION`
- `MIMALLOC_PURGE_DELAY`
- `NODE_USE_SYSTEM_CA`
- `NODE_EXTRA_CA_CERTS`
- `NODE_TLS_REJECT_UNAUTHORIZED`
- `HTTP_PROXY`
- `HTTPS_PROXY`
- `NO_PROXY`

Do not blindly add variables.

Determine whether a required variable was accidentally removed or changed.

---

### 4.5 Password and authentication

Verify that:

1. `CliBackendManager` generates the password.
2. The same password is provided to the CLI through `KILO_SERVER_PASSWORD`.
3. The same password is provided to `KiloConnectionService`.
4. `GetNswagClient()` receives the correct password.
5. the NSwag authentication extension is actually invoked;
6. the generated request contains:

```text
Authorization: Basic <base64(kilo:<password>)>
```

Do not hard-code credentials.

---

### 4.6 NSwag client initialization

Inspect `KiloConnectionService.cs`.

Verify:

- client construction;
- BaseUrl;
- password;
- HTTP transport;
- Newtonsoft.Json configuration;
- generated client initialization;
- lifetime of the client;
- request execution.

Verify that the generated client is using the runtime CLI BaseUrl rather than a static/default URL.

---

### 4.7 Health check

Determine whether the health request is actually sent.

Verify:

- generated health operation;
- request URL;
- HTTP method;
- authentication;
- response status;
- response deserialization;
- exception handling.

Distinguish between:

1. request not sent;
2. connection refused;
3. HTTP 401/403;
4. HTTP 404;
5. other HTTP error;
6. successful HTTP response with deserialization failure;
7. successful health check but incorrect connection state handling.

Do not simply catch and suppress the exception.

---

### 4.8 Connection state

Verify the state machine in `KiloConnectionService`.

Confirm that a successful health check transitions the service to the expected connected state.

Check:

- `connecting`;
- `connected`;
- `disconnected`;
- `error`.

Verify that no later initialization step immediately resets the state.

---

### 4.9 SSE initialization

After REST health succeeds, verify the SSE path.

Inspect `SseClient.cs`.

Confirm:

- correct BaseUrl;
- correct `/global/event` endpoint;
- authentication;
- connection establishment;
- retry behavior;
- cancellation handling.

Determine whether SSE failure prevents the service from becoming connected.

If REST connectivity succeeds but SSE prevents the extension from reaching its usable state, document this explicitly.

---

## 5. Historical Comparison

Use Git history to identify the regression.

Compare the current implementation against:

- the last known working Visual Studio implementation;
- the Kiota implementation;
- the NSwag migration commits;
- the Kiota cleanup commit.

Focus specifically on changes to:

```text
CliBackendManager.cs
KiloConnectionService.cs
SseClient.cs
VSProvider.cs
extension startup/package initialization
ApiClient/
project configuration
```

Identify the first commit where the connection behavior could have changed.

If the exact introducing commit cannot be established, document the narrowest known regression range.

---

## 6. Reproduction

Reproduce the problem using the actual Visual Studio Experimental/Debug instance.

The validation must not rely exclusively on unit tests or a normal build.

Capture enough diagnostic information to establish:

- whether the extension starts;
- whether the CLI starts;
- which port is discovered;
- whether the health request is attempted;
- HTTP status/exception if applicable;
- whether the service reaches `connected`;
- whether SSE starts.

Avoid logging the actual password.

If temporary diagnostic logging is required, remove unnecessary debug logging before completion or replace it with appropriate existing diagnostics.

---

## 7. Root Cause Classification

Classify the root cause as one of:

- `EXTENSION_STARTUP`
- `CLI_STARTUP`
- `ENVIRONMENT`
- `PORT_DISCOVERY`
- `AUTHENTICATION`
- `NSWAG_CLIENT`
- `HEALTH_CHECK`
- `CONNECTION_STATE`
- `SSE`
- `OTHER`

Explain why the selected classification is correct.

Do not claim NSwag is responsible merely because the migration happened recently.

---

## 8. Fix Requirements

Once the root cause is established:

- implement the smallest correct fix;
- preserve the NSwag architecture;
- preserve the existing hard-coded CLI path;
- preserve the existing CLI lifecycle behavior;
- preserve authentication semantics;
- do not reintroduce Kiota;
- do not introduce another HTTP client;
- do not refactor unrelated functionality.

If the regression is caused by an accidental removal during Kiota cleanup, restore the required behavior without restoring the obsolete Kiota infrastructure.

---

## 9. Validation

After the fix:

### Build

Build the Visual Studio extension and confirm:

- zero compilation errors;
- no new warnings attributable to the fix.

### Tests

Run the relevant test suite.

Report:

- passed;
- failed;
- skipped;
- pre-existing failures.

Do not disable tests to obtain a green build.

### Runtime

Launch the Visual Studio Experimental/Debug instance and verify:

- [ ] extension loads;
- [ ] CLI starts;
- [ ] CLI remains alive;
- [ ] listening port is detected;
- [ ] BaseUrl is correct;
- [ ] authentication succeeds;
- [ ] health request succeeds;
- [ ] `KiloConnectionService` reaches connected state;
- [ ] SSE connection is established;
- [ ] a normal API operation succeeds.

---

## 10. Regression Test

If the root cause can be covered by an automated test without overengineering, add a focused regression test.

Prefer testing the actual failure mechanism rather than merely asserting that a method was called.

Do not build a large test framework for this issue.

If the failure is inherently tied to the Visual Studio Experimental environment and cannot reasonably be automated, document that and retain the manual validation procedure.

---

## 11. Documentation

After the fix and validation:

Update the relevant task/porting documentation to record:

- root cause;
- introducing change/commit if identifiable;
- files modified;
- fix;
- build result;
- test result;
- Experimental instance runtime validation;
- any remaining known limitations.

Do not mark `CLEANUP-CLI-001` complete while this regression remains unresolved.

After this task is successfully completed, `CLEANUP-CLI-001` can resume its final validation/closure.

---

## 12. Out of Scope

The following are explicitly out of scope:

- CLI discovery;
- CLI downloading;
- JetBrains-style CLI installation;
- replacing NSwag;
- reintroducing Kiota;
- implementing new API endpoints;
- WebView integration;
- SSE event normalization;
- `drainPendingPrompts`;
- unrelated refactoring;
- performance optimization unrelated to the connection failure.

---

## 13. Completion Criteria

`BUG-CLI-001` may be marked `COMPLETE` only when:

1. The root cause of the connection failure is identified.
2. The regression is fixed.
3. The extension successfully connects to the CLI in a Visual Studio Experimental/Debug instance.
4. REST health succeeds through the NSwag client.
5. SSE is established when required by startup.
6. The project builds successfully.
7. Relevant tests pass, or unrelated pre-existing failures are explicitly documented.
8. No Kiota or legacy HTTP client is reintroduced.
9. The hard-coded CLI path remains unchanged.
10. Documentation accurately reflects the result.

---

## 14. Final Report

Provide a concise final report containing:

### Root cause

Exact technical cause of the connection failure.

### Regression point

Commit/change where the regression was introduced, if identifiable.

### Fix

Files and behavior changed.

### Runtime validation

Results from the Visual Studio Experimental/Debug instance.

### Tests

Build and test results.

### Architecture

Confirm that REST uses NSwag and SSE uses `SseClient`.

### Cleanup impact

Confirm whether the issue was related to the Kiota cleanup.

### Status

One of:

- `COMPLETE`
- `COMPLETE_WITH_NOTES`
- `BLOCKED`