# First 20 minutes: one visible result

**One objective:** see Atlas answer a request. Twenty minutes is a suggested session size, not a time limit.

Atlas is a backend. It returns data over HTTP; this repository does not contain a frontend website. JSON on your screen is a successful first result.

## Step 1: open a terminal in the repository

You should see `Atlas.slnx`, `src`, `tests`, and `astradocs`. Run:

```powershell
dotnet --version
```

You need a .NET 10 SDK. If the command is missing, use the [setup help](../docs/getting-started.md). If your SDK is installed under your Windows user profile but absent from PATH, the optional `python scripts/learn.py doctor` can locate it. Python is not needed to run the API.

## Step 2: start the development server

```powershell
dotnet run --project src/Atlas.Api --launch-profile http
```

The checked-in HTTP profile uses `http://localhost:5003` and the Development environment. Confirm the actual listening address printed in the terminal. Development creates and seeds a local SQLite database when needed.

Leave this terminal open. It is running the server. Open a second terminal for requests.

## Step 3: ask whether the host responds

```powershell
curl.exe -i http://localhost:5003/health/live
```

On Windows, `curl.exe` explicitly selects the curl program rather than a PowerShell alias. Look for HTTP 200 and a JSON body with `status` set to `healthy`. This probe needs no API key.

Pause here. You have sent a request to a running backend and received its response.

## Step 4: read a seeded country

```powershell
curl.exe -i -H "X-Api-Key: dev-admin-key" http://localhost:5003/api/countries/PH
```

Find `code`, `name`, and `currencyCode` in the response. They should describe the seeded Philippines record. The development key identifies a platform admin.

Repeat without the header:

```powershell
curl.exe -i http://localhost:5003/api/countries/PH
```

Expect 401. The country did not disappear; this request lacks an accepted identity.

## Say it again

The server is the running program. The request is your message to it. The API key identifies the caller. The response tells you what happened.

## Checkpoint

Write: “I sent GET to __. With a key I received __. Without a key I received __.” You can stop for today after filling those blanks.

If connection fails, check that the first terminal is still running and the port matches. If the country is missing, confirm the `http` Development profile and inspect startup errors; do not delete an existing database to guess at a fix. If 401 appears with a key, check the header and which environment/database you started.

Press Ctrl+C in the server terminal when you want to stop it. Next: [the story](01-the-story.md).
