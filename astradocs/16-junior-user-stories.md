# 25 junior user stories with step-by-step implementation plans

**Status: implementation in progress.** This workbook preserves the original story requirements and implementation plans. Consult the [bootcamp progress register](bootcamp/progress.md) for implemented and verified behavior; acceptance criteria alone are not a claim that a feature is complete.

Each story is deliberately small. Most change one endpoint plus its tests and reference documentation. The last three also change the existing report service. None needs a new database table, migration, background worker, external service, or deployment. You can complete one story over several sessions.

## How to follow a story

1. Read its user story and example before opening code.
2. Open the listed files and find the named handler or method.
3. Write down the expected response for the example.
4. Prepare only the data the story needs in a test database.
5. Add a test that distinguishes the proposed behavior from current behavior.
6. Follow the numbered implementation plan one step at a time.
7. Run the focused verification command, then review the diff.
8. Update the relevant section of [the API reference](../docs/api-reference.md) and explain your change to a reviewer.

The repetition in the plans is intentional: identify the boundary, add the smallest rule or filter, preserve authorization, and verify the result. “Done” means the acceptance criteria are demonstrated, not merely that the code compiles. Time estimates are omitted so you can work at your own pace.

**Dependencies:** all 25 can start from the current checkout. If you do them in order, preserve earlier additions when editing the same handler. Do not replace a whole handler with an older example and accidentally remove a previous filter. Stories 20 and 21 both concern documents, but neither requires the other. No later story requires completing every earlier one.

## Pick a story

| ID | User-visible result | Main practice |
| --- | --- | --- |
| [JS-01](#js-01-limit-country-name-length) | Clear error for an overlong country name | One validation boundary |
| [JS-02](#js-02-validate-country-currency-letters) | Country currency contains three letters | Character validation |
| [JS-03](#js-03-limit-client-display-and-legal-names) | Client names fit their intended limits | Optional-field fallback |
| [JS-04](#js-04-limit-worker-name-length) | Clear error for an overlong worker name | Repeat a familiar pattern |
| [JS-05](#js-05-limit-compliance-document-name-length) | Clear error for an overlong document name | Validation on a nested route |
| [JS-06](#js-06-reject-undefined-compliance-document-types) | Invalid numeric document types are rejected | Enum validity |
| [JS-07](#js-07-reject-undefined-contract-status-filters) | Invalid status filters are rejected | Query validation |
| [JS-08](#js-08-reject-future-worker-birth-dates) | Future dates of birth are rejected | An injected clock |
| [JS-09](#js-09-filter-countries-by-currency) | Countries can be narrowed by currency | A simple SQL filter |
| [JS-10](#js-10-filter-countries-by-active-state) | Active and inactive countries can be listed separately | Optional Boolean input |
| [JS-11](#js-11-find-clients-by-name-prefix) | Client names can be searched by prefix | Filtering within client scope |
| [JS-12](#js-12-find-workers-by-name-prefix) | Worker names can be searched by prefix | Combining existing filters |
| [JS-13](#js-13-order-same-name-clients-consistently) | Equal-name clients have an explicit paging order | A unique tie-breaker |
| [JS-14](#js-14-order-same-name-workers-consistently) | Equal-name workers have an explicit paging order | Repeat ordering with tenant checks |
| [JS-15](#js-15-filter-contracts-by-country) | Contract lists can be narrowed by employment country | Combining tenant and business filters |
| [JS-16](#js-16-filter-invoices-by-payroll-run) | A caller can list visible invoices for one run | GUID filtering |
| [JS-17](#js-17-filter-payroll-runs-by-year-and-month) | A platform admin can find a payroll period | Dependent query parameters |
| [JS-18](#js-18-filter-fx-rates-by-effective-date-range) | FX history can be narrowed to a date range | Inclusive date bounds |
| [JS-19](#js-19-show-only-required-onboarding-items) | A checklist can hide optional items | Filtering without changing its summary |
| [JS-20](#js-20-filter-a-workers-documents-by-type) | A worker's document list can show one type | Reusing enum knowledge |
| [JS-21](#js-21-fetch-one-worker-document) | A created document's location can be fetched | One read-only detail route |
| [JS-22](#js-22-filter-salary-history-through-a-date) | Salary-history entries can be limited by effective date | Read-only history filtering |
| [JS-23](#js-23-filter-compliance-expiry-reports-by-country) | An expiry report can focus on one country | Endpoint-to-service parameters |
| [JS-24](#js-24-filter-headcount-reports-by-country) | Headcount and both breakdowns use one country scope | Filtering before aggregation |
| [JS-25](#js-25-filter-payroll-cost-reports-by-run-status) | Draft and completed payroll costs can be viewed separately | Enum filtering before aggregation |

## Shared setup recipes

These recipes keep the individual plans small enough to follow. They describe test setup, not new application features you must build.

- **Country:** follow [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs). Send otherwise-valid name, currency, rates, and country code. Use an unused code for each successful creation in a shared fixture. Test names and currencies can be fictional; assigned-code lookup is not part of these stories.
- **Client and worker:** follow [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs). Seed a supported country first. A client's headquarters and a worker's country must refer to that data. Give workers different email addresses.
- **Draft contract:** follow `CreateDraftContractAsync` in [OnboardingEndpointTests.cs](../tests/Atlas.Tests/Integration/OnboardingEndpointTests.cs). Create the client and worker, then the contract; default onboarding is created automatically.
- **Active contract:** follow `CreateContractAsync` in [ReportEndpointTests.cs](../tests/Atlas.Tests/Integration/ReportEndpointTests.cs). Complete required items before calling activate. Helpers in another test class are private: use them as a reading example or copy a small relevant setup pattern into your class, not as directly callable shared methods.
- **Client-scoped caller:** follow [AuthorizationTests.cs](../tests/Atlas.Tests/Integration/AuthorizationTests.cs). Create two companies, issue an API user for company A using the admin client, then call `CreateClientWithApiKey` with that key. Create a separate viewer key when the story asks for a viewer. Development keys are not automatically present in tests.
- **Worker documents:** follow `CreateWorkerAsync` and the POST examples in [ComplianceEndpointTests.cs](../tests/Atlas.Tests/Integration/ComplianceEndpointTests.cs). Dates are optional; omit expiry unless the test needs it, avoiding an unrelated date boundary.
- **Payslips and invoices:** follow `CreateCountryAsync`, `HireAsync`, and the completion example in [PayrollEndpointTests.cs](../tests/Atlas.Tests/Integration/PayrollEndpointTests.cs). Creating a run produces payslips; completing it produces invoices. Use the same local and billing currency to avoid unrelated FX setup. One country/month cannot have two runs.
- **FX history:** follow `AddRateAsync` in [FxAndMultiCurrencyTests.cs](../tests/Atlas.Tests/Integration/FxAndMultiCurrencyTests.cs). A base/quote/date combination is unique. Different dates can have different rates for the same pair.

For assertions over an entire result set, use a fresh `AtlasApiFactory` inside the test, as demonstrated in [ReportQueryTests.cs](../tests/Atlas.Tests/Integration/ReportQueryTests.cs), or narrowly identify your own seeded rows. A shared factory can retain other tests' data. Do not assume its count is zero.

### Common verification rules

- For writes rejected by new validation, check 400, `application/problem+json`, the expected `errors` key, and that no new matching row was saved. Keep all other request fields valid.
- For list filters, check returned IDs and the filtered `X-Total-Count`, not just HTTP 200. Apply filters before `ToPageAsync` so totals and pages use the same query.
- For client-scoped reads, create a matching row for another company and prove it does not appear. An authenticated key alone is not sufficient evidence of correct scope.
- For report routes, preserve platform-only authorization and the current JSON shape. Reports do not use the list-pagination headers.
- Run the story's command from the repository root after implementation. The commands below are instructions for your future work; they have not been run as implementation verification for these unimplemented stories.
- If a test class already covers a listed example, keep or extend it rather than adding a duplicate just to increase the test count. After focused tests pass, run `dotnet test Atlas.slnx` before handing over the change.

---

## JS-01: Limit country name length

**User story:** As a platform admin creating a country, I want an overlong name to receive a clear field error so I can correct it before saving.

**Current behavior and small scope:** the country POST handler rejects blank names but does not explicitly enforce the 100-character length configured for `Country.Name`. Add API validation only. Measure the trimmed string using C# `Length`; this exercise does not introduce grapheme counting or change the database model.

**Open:** [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs), [AtlasDbContext.cs](../src/Atlas.Infrastructure/AtlasDbContext.cs), and [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs). Find `request.Name`, then the existing `errors` dictionary.

**Acceptance criteria:**

- A name of 100 ASCII `A` characters returns 201 when other values are valid and the code is unused.
- A name of 101 `A` characters returns 400 with `errors.name` and creates no country.
- Two surrounding spaces around the 100-character name do not count toward the limit; the saved name is trimmed.
- Null, empty, and whitespace-only names still return 400. Creating countries remains platform-only.

**Implementation plan:**

1. Run the existing country suite and read its successful creation example.
2. Add two tests using different unused codes and names made with `new string('A', 100)` and `new string('A', 101)`. Keep currency and rates valid.
3. In the name-validation block, keep the blank check first. Only access the trimmed name after it is known to exist.
4. Add the over-100 condition to the same `errors["name"]` path. Use a message stating the maximum; do not truncate the name silently.
5. Leave the later object construction and `SaveChangesAsync` in place. The existing early return should reject the request before those lines.
6. Add the padded-boundary case and confirm stored/returned trimming. Inspect the database or a follow-up lookup for the rejected code to prove it was not created.
7. Document the trimmed 100-character limit in the country-create API section.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~CountryEndpointTests`. Suggested test name: `CreateCountry_NameOver100_ReturnsNameError`.

**Done / explain:** show one accepted boundary and one rejected boundary. Explain why a length annotation on an entity mapping is not a substitute for the explicit HTTP validation you added.

## JS-02: Validate country currency letters

**User story:** As a platform admin, I want a country's currency code to contain three letters so obvious typing mistakes are rejected with a useful error.

**Current behavior and small scope:** country creation currently checks trimmed currency length, so a three-character value such as `U1D` can pass that check. Change only the country-create currency field. Do not alter FX-rate creation, client billing currency, country-code validation, or introduce an external list of currencies.

**Open:** [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs), especially `request.CurrencyCode`, and [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs).

**Acceptance criteria:**

- `usd` and ` USD ` are accepted and stored as `USD` for an otherwise valid creation.
- `U1D`, `US!`, and a three-character string containing a non-ASCII letter return 400 with `errors.currencyCode`.
- Blank values and lengths other than three remain invalid.
- Fictional three-letter ASCII codes remain allowed. The rule checks shape, not whether a currency is officially assigned.

**Implementation plan:**

1. Add a failing `U1D` test with an unused country code. Assert the currency error key and absence of a saved country.
2. Find the existing currency blank/length guard. Keep safe handling of null and whitespace.
3. Work with the trimmed, uppercase value for the character check. Require each character to fall in the `A` through `Z` range.
4. Add the character failure to the existing currency error. Avoid a generic “all letters” helper unless you confirm it enforces ASCII rather than a wider alphabet.
5. Retain the existing uppercase assignment when constructing the entity. Do not change the response record or rates.
6. Test lowercase, padding, punctuation, digits, and one non-ASCII input. Use separate successful country codes so duplicate detection does not hide the intended behavior.
7. Update the API reference to say “three ASCII letters, normalized to uppercase”; make the shape-only limitation clear.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~CountryEndpointTests`. Suggested test name: `CreateCountry_CurrencyWithDigit_ReturnsCurrencyError`.

**Done / explain:** explain the difference between length validation, character validation, and checking membership in an official currency list. This story implements only the first two.

## JS-03: Limit client display and legal names

**User story:** As a platform admin creating a client, I want clear length errors for company names so the stored display and legal names fit their intended limits.

**Current behavior and small scope:** client creation checks a nonblank display name and falls back to that name when `legalName` is blank. Both fields have a 200-character mapping limit. Preserve the fallback and add checks on the trimmed values; no other client field changes.

**Open:** [ClientEndpoints.cs](../src/Atlas.Api/Endpoints/ClientEndpoints.cs), [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs), and the Client mapping in [AtlasDbContext.cs](../src/Atlas.Infrastructure/AtlasDbContext.cs).

**Acceptance criteria:**

- Each name accepts 200 characters and rejects 201 after trimming.
- An overlong display name produces `errors.name`; an overlong explicit legal name produces `errors.legalName`.
- Omitted or whitespace legal name still becomes the valid trimmed display name.
- Invalid creation saves no client. Billing email, fee defaults, currency defaults, and platform-only authorization retain their behavior.

**Implementation plan:**

1. Seed a headquarters country using the shared recipe. Prepare a valid billing email and omit optional fee/currency overrides.
2. Add a display-name-over-limit test with a short legal name. Add a separate legal-name-over-limit test with a short display name.
3. Keep the display name's existing required check, then add its trimmed length check.
4. For the legal name, apply the limit only when it is nonblank. Do not turn this optional field into a required one.
5. Reuse the current error dictionary and its early validation return. Keep the later fallback expression in the entity construction.
6. Test the exact boundaries plus an omitted legal name and a whitespace legal name. Fetch the created client to verify the fallback value was saved.
7. Add both limits and the preserved fallback behavior to the API reference.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~ClientAndWorkerEndpointTests`. Suggested test names: `CreateClient_LongLegalName_ReturnsLegalNameError` and `CreateClient_BlankLegalName_UsesDisplayName`.

**Done / explain:** identify the difference between validating an optional supplied value and demanding that the value be supplied. Show that you checked the two fields separately.

## JS-04: Limit worker name length

**User story:** As a platform admin adding a worker, I want a clear error when the full name is too long so I can fix the input without guessing why it failed.

**Current behavior and small scope:** the worker POST handler rejects blank names; the model mapping has a 200-character name limit. Add a trimmed length check while preserving email normalization and the supported-country check.

**Open:** [WorkerEndpoints.cs](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs) and [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs). Find `request.FullName` in the POST handler, not the list handler.

**Acceptance criteria:**

- A 200-character ASCII name is accepted; 201 characters returns `errors.fullName` with 400.
- Surrounding whitespace is removed before measuring and saving.
- Null/blank names remain invalid, and rejected requests do not create worker rows.
- Valid emails still normalize to lowercase, and duplicate-email behavior remains 409.

**Implementation plan:**

1. Seed one supported country. Give each successful test worker a different email.
2. Add a test using a valid email/country and a name of 201 `A` characters. Confirm the new expected field error does not pass yet.
3. Keep the existing blank-name guard. Add a separate length branch only after the value is nonblank.
4. Write to `errors["fullName"]`; do not rename the public field to `name` or use a domain conflict response for malformed input.
5. Leave the existing trim during entity construction in place. Do not truncate a user's name automatically.
6. Add exact-boundary and padded-boundary tests. Re-run the existing email normalization and duplicate tests to check nearby behavior.
7. Document the limit in the worker-create section and review the diff for changes outside the name guard and its tests.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~ClientAndWorkerEndpointTests`. Suggested test name: `CreateWorker_LongFullName_ReturnsFullNameError`.

**Done / explain:** explain why the new failure is 400 instead of 409. Point to the early return that prevents `Add` and `SaveChangesAsync` on an invalid request.

## JS-05: Limit compliance document name length

**User story:** As a platform admin adding a worker's compliance document, I want an overlong document name rejected clearly so its stored label stays within the intended size.

**Current behavior and small scope:** the document POST handler checks for a nonblank name but not the mapped 200-character limit. Add the length rule only; do not change dates, document types, or file storage.

**Open:** [ComplianceEndpoints.cs](../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs), the document mapping in [AtlasDbContext.cs](../src/Atlas.Infrastructure/AtlasDbContext.cs), and [ComplianceEndpointTests.cs](../tests/Atlas.Tests/Integration/ComplianceEndpointTests.cs).

**Acceptance criteria:**

- For an existing worker, 200 trimmed characters is valid; 201 returns 400 with `errors.name`.
- Null/blank names remain invalid; padding around a valid boundary is accepted and trimmed.
- An unknown worker still follows the existing 404 path.
- Document creation remains platform-only. A rejected request creates no document for that worker.

**Implementation plan:**

1. Create a worker with the shared recipe. Use `Passport` as a valid type and omit dates to isolate name validation.
2. Add the over-limit test against `/api/workers/{workerId}/documents`. Record the worker's document count before the POST.
3. In `workerDocs.MapPost`, leave the worker existence check in its current place.
4. Extend the name-validation block with a trimmed length check, using the current error dictionary and 400 response path.
5. Keep the existing name trim and document construction unchanged for valid requests.
6. Test 200, 201, and padded 200 characters. After rejection, fetch this worker's document list and confirm its count did not increase.
7. Retain or run the existing unknown-worker and invalid-date examples, then document the name limit on the nested POST route.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~ComplianceEndpointTests`. Suggested test name: `AddDocument_LongName_ReturnsNameErrorWithoutSaving`.

**Done / explain:** explain why the worker must exist before this name test can demonstrate the intended validation. An unrelated 404 would not prove your new rule works.

## JS-06: Reject undefined compliance document types

**User story:** As a platform admin, I want unsupported document-type values rejected so a document cannot be created with a type label the application does not understand.

**Current behavior and small scope:** the POST handler uses `Enum.TryParse`, which can parse numeric values that are not named members. Validate that the parsed value is defined in `ComplianceDocumentType`. Preserve the existing accepted numeric representations of defined values; making the API names-only is a separate decision.

**Open:** [ComplianceEndpoints.cs](../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs), [ComplianceDocument.cs](../src/Atlas.Domain/Entities/ComplianceDocument.cs), and [ComplianceEndpointTests.cs](../tests/Atlas.Tests/Integration/ComplianceEndpointTests.cs).

**Acceptance criteria:**

- `Passport`, `passport`, and the string `0` are accepted as Passport.
- Strings `999`, `-1`, and `not-a-type` return 400 with `errors.type`.
- Missing and blank types remain invalid; rejected requests save no document.
- The valid enum names and existing response shape do not change.

**Implementation plan:**

1. Read the enum and write down its current members and numeric values. Prepare an existing worker and valid document name.
2. Extend the invalid-type integration examples with the string `999`. Send a JSON string, not a JSON number, because the request record's Type property is a string.
3. Retain the parsed enum value from `Enum.TryParse` instead of discarding it. Add an `Enum.IsDefined` check after successful parsing.
4. Use the current `errors["type"]` response for failure. Ensure parsing and the defined-value check must both succeed.
5. Avoid using an unassigned parsed variable after a short-circuit branch; keep the guard flow readable. The existing parse in construction may remain for this small story.
6. Test a lowercase name and a defined numeric string, then confirm the returned type is the canonical name `Passport`.
7. Document the supported values and numeric compatibility; do not claim the API now forbids all numbers.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~ComplianceEndpointTests`. Suggested test name: `AddDocument_UndefinedNumericType_ReturnsTypeError`.

**Done / explain:** explain why successfully parsing an enum does not necessarily mean the value is a declared member. Distinguish the JSON field type from the enum value it represents.

## JS-07: Reject undefined contract status filters

**User story:** As an authenticated caller listing contracts, I want an invalid status filter to return a clear error so I do not mistake a bad filter for an empty business result.

**Current behavior and small scope:** the contract list parses `status` but does not separately require a defined enum member. Keep valid names and their existing numeric forms; add defined-value validation only.

**Open:** [ContractEndpoints.cs](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), [EmploymentContract.cs](../src/Atlas.Domain/Entities/EmploymentContract.cs), and [ContractEndpointTests.cs](../tests/Atlas.Tests/Integration/ContractEndpointTests.cs).

**Acceptance criteria:**

- `status=Draft`, `status=draft`, and `status=0` select Draft contracts visible to the caller.
- `status=999` and `status=-1` return 400 with `errors.status`, rather than a successful empty list.
- An omitted or whitespace-only status retains the existing unfiltered behavior.
- Existing `clientId`, pagination, and caller scoping continue to apply.

**Implementation plan:**

1. Locate the existing `Enum.TryParse<ContractStatus>` block inside the list handler.
2. Add an invalid numeric filter test using an authenticated client; no contract data is necessary for that specific validation test.
3. Add the defined-enum check to the same failure condition and keep the current validation error key.
4. Apply the existing status `Where` only after both parsing and membership checks succeed.
5. For successful filtering, seed one Draft and one Active contract using separate workers. Complete onboarding when creating the Active example.
6. Compare returned IDs for `Draft`, `draft`, and `0`. Add another client's matching Draft record and confirm a client-scoped caller cannot see it.
7. Update the contract-list reference and leave entity transition methods unchanged.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ContractEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`. Suggested test name: `ListContracts_UndefinedNumericStatus_ReturnsStatusError`.

**Done / explain:** explain the difference between “a valid filter matched no rows” and “the supplied filter is invalid.” Show the test data that proves successful filtering really selects a subset.

## JS-08: Reject future worker birth dates

**User story:** As a platform admin registering a worker, I want a future date of birth rejected so an obvious date-entry mistake does not become stored worker data.

**Current behavior and small scope:** worker creation stores the optional birth date without this check. Add only “not later than the current UTC date.” This is not a minimum-age policy, age calculation, or country-specific hiring rule.

**Open:** [WorkerEndpoints.cs](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs), the existing TimeProvider registration in [Program.cs](../src/Atlas.Api/Program.cs), [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs), and the fixed-clock example in [ReportQueryTests.cs](../tests/Atlas.Tests/Integration/ReportQueryTests.cs).

**Acceptance criteria:**

- With the test clock fixed at 2026-06-15 UTC, 2026-06-16 returns 400 with `errors.dateOfBirth`.
- 2026-06-15 and an earlier date are accepted by this rule. Omitted/null dates remain accepted.
- The saved value is the supplied calendar date, with no timezone conversion of DateOnly.
- Name, email, country, duplicate detection, and authorization behavior remain intact.

**Implementation plan:**

1. Add a focused birth-date test with a fixed TimeProvider using the existing report test as a pattern. Keep a valid supported country and distinct worker emails.
2. Add `TimeProvider clock` as a dependency parameter to the worker POST handler. Use the existing registration rather than registering a second system clock.
3. Obtain the UTC calendar date from `clock.GetUtcNow().UtcDateTime` once for validation.
4. When the supplied birth date is non-null and after that date, add a `dateOfBirth` error to the existing dictionary.
5. Preserve the current early validation return and entity assignment. Do not fill in an omitted date with today's date.
6. Test tomorrow, today, an earlier date, and null using the same fixed clock. Include a leap-day value as an ordinary valid past date.
7. Document the UTC date rule and its intentionally limited scope.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~ClientAndWorkerEndpointTests`. Suggested test name: `CreateWorker_FutureBirthDate_ReturnsDateOfBirthError`.

**Done / explain:** explain why the test chooses its clock instead of sleeping until tomorrow, and why the rule should not be described as proving a worker is old enough to hire.

## JS-09: Filter countries by currency

**User story:** As an authenticated caller choosing a hiring country, I want to list countries using one currency so I can narrow the options without filtering the response myself.

**Current behavior and small scope:** the country list supports paging but no currency filter. Add the optional query parameter `currencyCode`. Use the existing `Country.CurrencyCode` column and existing response; no schema change.

**Open:** [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs), [Pagination.cs](../src/Atlas.Api/Pagination.cs), and [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs).

**Acceptance criteria:**

- `GET /api/countries?currencyCode=USD` returns only USD countries.
- `usd` and padded ` usd ` mean USD. Missing/blank input means no currency restriction.
- A supplied nonblank value must normalize to three ASCII letters; invalid shape returns 400 on `currencyCode`. A well-shaped unknown value such as `ZZZ` returns 200 with an empty array.
- Paging totals count only matching countries. Authentication and country-code ordering remain unchanged.

**Implementation plan:**

1. Use a fresh test factory and seed two countries with USD and one with EUR. Choose distinct country codes.
2. Add a test requesting USD with `pageSize=1`. Expect one body item but `X-Total-Count` equal to two.
3. Add `string? currencyCode` to the country list handler's parameters. Keep existing paging validation.
4. When the value is nonblank, trim/uppercase it and validate its three-letter shape. Treat blank as an absent filter, not a create-request error.
5. Start from a queryable countries query. Add the currency `Where` only when there is a normalized value.
6. Apply the existing order and `ToPageAsync` to that filtered query. Do not fetch a page first and then remove nonmatching countries.
7. Test EUR, ZZZ, lowercase/padding, blank, and invalid shape. Verify an unfiltered request still returns all three fixture records.
8. Document the new parameter and its empty-result behavior.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~CountryEndpointTests`. Suggested test name: `ListCountries_CurrencyFilter_UpdatesRowsAndTotalCount`.

**Done / explain:** show the filtered count header and the one-item page. Explain why filtering after pagination could produce a short page and an incorrect total.

## JS-10: Filter countries by active state

**User story:** As an authenticated caller viewing supported countries, I want to choose active or inactive entries so I can show the appropriate list for my task.

**Current behavior and small scope:** `Country.IsActive` already exists and is returned, but the list cannot filter by it. Add optional `isActive`; do not add activation/deactivation write endpoints or change existing hiring rules.

**Open:** [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs), [Country.cs](../src/Atlas.Domain/Entities/Country.cs), and [CountryEndpointTests.cs](../tests/Atlas.Tests/Integration/CountryEndpointTests.cs).

**Acceptance criteria:**

- `?isActive=true` returns only active records; `?isActive=false` returns only inactive records.
- Omitting `isActive` returns both. The value false is an actual filter, not the absence of a filter.
- An invalid Boolean such as `?isActive=maybe` receives the framework's 400 binding response; this story does not require a custom per-field body for binding failures.
- Pagination headers reflect the filtered query. If JS-09 is implemented, currency and active-state filters both apply.

**Implementation plan:**

1. Seed three countries in an isolated test: two active and one inactive. Use `factory.WithDb` to set the inactive fixture value; this is test setup, not a new public feature.
2. Add `bool? isActive` to the GET list handler. Nullable distinguishes omitted from false.
3. Keep the query-building pattern from the current handler or JS-09. Add `Where` when `isActive` is non-null, comparing the property to the supplied Boolean.
4. Do not use a condition that runs only when the parameter is true; that would ignore the false filter.
5. Preserve any previously added currency filter, ordering, and pagination. Pass the final filtered query to the paging helper once.
6. Test true, false, omitted, and invalid text. Check the returned `IsActive` values and total count for each valid case.
7. If combining with JS-09, make the inactive country EUR and request both `currencyCode=USD` and `isActive=false`; expect an empty result and zero total.
8. Document the three distinct states: omitted, true, and false.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~CountryEndpointTests`. Suggested test name: `ListCountries_IsActiveFalse_ReturnsInactiveRows`.

**Done / explain:** explain why a nullable Boolean is needed. Show the test that would fail if false were mistakenly treated as “do not filter.”

## JS-11: Find clients by name prefix

**User story:** As a caller listing clients I am allowed to see, I want to search the start of a company name so I can locate it in a longer list.

**Current behavior and small scope:** the client list sorts names but has no text filter. Add optional `namePrefix`, applied to the display Name only. Define case-insensitive matching for ASCII letters; accented-letter folding, fuzzy matching, and searching LegalName are outside this exercise.

**Open:** [ClientEndpoints.cs](../src/Atlas.Api/Endpoints/ClientEndpoints.cs), [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs), and the client-scoped test setup in [AuthorizationTests.cs](../tests/Atlas.Tests/Integration/AuthorizationTests.cs).

**Acceptance criteria:**

- `namePrefix=ac` matches `Acme Robotics` and `AC Tools`, but not `Beta Acme`.
- Trim the prefix; omitted or whitespace input leaves the list unfiltered. More than 200 trimmed characters returns 400 on `namePrefix`.
- `%` and `_` are literal characters in the prefix, not caller-controlled wildcard operators.
- A client user still sees only its own matching client. Total-count headers use the scoped, filtered query.

**Implementation plan:**

1. Create the three named clients above in a fresh fixture. Add a fourth whose name starts with a literal `%` for the wildcard case.
2. Add `string? namePrefix` to the list handler. Normalize nonblank input by trimming and uppercasing it; validate the length before querying.
3. Retain the existing caller-scope `Where`. Apply the prefix filter in addition to it, never in place of it.
4. Use a query expression comparing an uppercased name with the normalized prefix via `StartsWith`. Keep it before materialization; verify it executes with the real SQLite provider. Avoid the `StringComparison` overload in the database expression and avoid composing a raw LIKE wildcard pattern.
5. Preserve the current name ordering, any tie-breaker from JS-13, and `ToPageAsync`.
6. Test prefix rather than substring behavior, ASCII case, padding, blank, overlong input, and a literal wildcard character.
7. Issue a client-scoped key for one of the matching companies. Assert the other matching company's ID is absent and the total is one.
8. Document the prefix-only and ASCII case behavior without claiming international search or improved performance.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ClientAndWorkerEndpointTests|FullyQualifiedName~AuthorizationTests"`.

**Done / explain:** show that a matching name cannot broaden a client's scope. Explain why `StartsWith` is different from substring matching.

## JS-12: Find workers by name prefix

**User story:** As a caller viewing workers, I want to search the beginning of their full names so I can find the right worker while keeping existing country and client restrictions.

**Current behavior and small scope:** the worker list already filters by country and scopes workers through the caller's contracts. Add optional `namePrefix` on FullName using the same ASCII case-insensitive prefix contract as JS-11. JS-11 is a reference, not a dependency.

**Open:** [WorkerEndpoints.cs](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs), [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs), and [AuthorizationMatrixTests.cs](../tests/Atlas.Tests/Integration/AuthorizationMatrixTests.cs).

**Acceptance criteria:**

- `namePrefix=sa` matches `Sam Lee` and `SARA Jones`, but not `Alex Sam`.
- Trim input; blank/omitted means no name filter. More than 200 trimmed characters returns 400 on `namePrefix`.
- Country and name filters combine. Client callers never gain access to an unrelated worker with a matching name.
- Count headers, literal `%`/`_` handling, and existing sort behavior remain consistent with the documented prefix contract.

**Implementation plan:**

1. Create Sam and Sara in different countries and Alex in Sam's country. Give each a unique email.
2. Add `string? namePrefix` to the worker list handler and its trimmed length validation.
3. Keep the existing country filter and the `db.Contracts.Any(...)` caller-scope predicate exactly as applicable.
4. Add an extra name predicate using an uppercased FullName and normalized prefix with queryable `StartsWith`; keep it before ordering and paging.
5. Test `sa` alone, `sa` with Sam's country, and an unknown prefix. Assert IDs and filtered totals, not just the number of body elements.
6. For scope testing, create separate contracts associating Sam and Sara with different clients. Give a client viewer key access to only one, then search for the shared prefix.
7. Test blank, overlong, mixed-case, and a literal wildcard prefix. Preserve a stable tie-breaker if JS-14 was already completed.
8. Document the new parameter and clarify that matching is over FullName, not email.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ClientAndWorkerEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** name the three independent restrictions that can be present: name prefix, country, and the caller's client. Explain why they must all remain in the query.

## JS-13: Order same-name clients consistently

**User story:** As a platform admin paging through companies, I want clients with identical display names to have an explicit order so the API defines which record comes first.

**Current behavior and small scope:** the list orders only by Name. Add ascending Id as a secondary key. This defines ordering in a stable dataset; it does not provide a snapshot across concurrent inserts or change offset pagination to cursor pagination.

**Open:** [ClientEndpoints.cs](../src/Atlas.Api/Endpoints/ClientEndpoints.cs), [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs), and [Pagination.cs](../src/Atlas.Api/Pagination.cs).

**Acceptance criteria:**

- Display name remains the primary ascending sort key; Id is the secondary ascending key.
- Three equal-name fixture records have predictable page membership when requesting one per page.
- The count remains three for the isolated set, and no ID repeats across those pages.
- Tenant scope and any existing name-prefix filter remain intact. No promise is made about a dataset changing between requests.

**Implementation plan:**

1. In a fresh factory, seed a country and three Clients directly through `WithDb`, setting their Id values at construction. Use complete valid required properties from the entity/example setup.
2. Choose IDs identical except for final suffixes `000000000003`, `000000000001`, and `000000000002`, with the same display name. For example, the first full ID can be `00000000-0000-0000-0000-000000000003`. Insert them in that order; do not change primary keys after saving.
3. Read pages 1, 2, and 3 with `pageSize=1`. Assert the ID suffix order 1, 2, 3 and total count three.
4. Change the query's sort to Name followed by Id using `ThenBy`, before `ToPageAsync`. Keep the first `OrderBy`; a second `OrderBy` would replace it.
5. Add a differently named record in a separate example to prove the primary name sort is still respected.
6. Inspect the query during debugging or review to confirm both order keys are present. The old implementation might happen to produce the expected equal-name order on a particular database plan; do not manufacture a timing-based failure.
7. Re-run client-scope tests and document the secondary sort key.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ClientAndWorkerEndpointTests|FullyQualifiedName~AuthorizationTests"`.

**Done / explain:** explain why an observed order is not the same as an explicitly defined order. State the concurrency limitation of offset pagination.

## JS-14: Order same-name workers consistently

**User story:** As a caller paging through workers, I want records with the same full name to use a predictable secondary order so I can distinguish them reliably in a stable list.

**Current behavior and small scope:** worker listing orders only by FullName. Add Id ascending as its tie-breaker, preserving country filtering and the contract-based visibility predicate. This story can be done without JS-13.

**Open:** [WorkerEndpoints.cs](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs), [ClientAndWorkerEndpointTests.cs](../tests/Atlas.Tests/Integration/ClientAndWorkerEndpointTests.cs), and [AuthorizationMatrixTests.cs](../tests/Atlas.Tests/Integration/AuthorizationMatrixTests.cs).

**Acceptance criteria:**

- FullName remains primary; Id is secondary.
- Three same-name workers with distinct emails can be paged without repeated IDs in an unchanged fixture, in the documented ID order.
- Country filtering still works and total headers still count the filtered rows.
- A same-name worker belonging only to another client remains invisible to a client user.

**Implementation plan:**

1. Seed a country and three Workers with a shared FullName, different emails, and fixed IDs that differ only by final suffix 3, 1, 2. Set IDs at creation through `WithDb`.
2. Add a paged test expecting suffixes 1, 2, 3 and matching total count. Keep unrelated workers out by using a fresh factory.
3. Find the final worker `OrderBy(w => w.FullName)` and append the Id `ThenBy` before paging.
4. Leave the earlier country and client-scope predicates in place. Preserve the name-prefix predicate if JS-12 is already implemented.
5. Add a test with a differently named worker to keep the primary sorting requirement visible.
6. Create contracts linking same-name workers to different clients; use a client-scoped key and confirm only authorized IDs appear in the pages.
7. Inspect the defined sort in the code/query as well as the result. As with JS-13, an old query may appear stable by accident on small data.
8. Document FullName then Id ordering and the limitation when records change between page requests.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ClientAndWorkerEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** explain why two people having the same name is valid data and why the identifier is a suitable final tie-breaker. Do not make FullName unique to solve sorting.

## JS-15: Filter contracts by country

**User story:** As a caller viewing contracts, I want to restrict the list to an employment country so I can inspect one location while keeping my existing client permissions.

**Current behavior and small scope:** contract listing supports clientId and status, but not country. Add optional `countryCode` using the contract's stored CountryCode. This is an employment-country filter, not the client's headquarters country.

**Open:** [ContractEndpoints.cs](../src/Atlas.Api/Endpoints/ContractEndpoints.cs), [EmploymentContract.cs](../src/Atlas.Domain/Entities/EmploymentContract.cs), and [ContractEndpointTests.cs](../tests/Atlas.Tests/Integration/ContractEndpointTests.cs).

**Acceptance criteria:**

- `countryCode=PH` selects contracts whose CountryCode is PH; lowercase and padding normalize.
- Blank or omitted means no country filter. A supplied nonblank value must contain two ASCII letters; invalid shape returns 400 on `countryCode`, unknown valid shape returns an empty list.
- Country, status, and clientId combine with existing caller scope.
- Result shape and filtered pagination headers remain unchanged.

**Implementation plan:**

1. In a fresh fixture, create two supported countries and workers in each. Create contracts so the same company has one in each country.
2. Add another company's contract in the first country for the scope test.
3. Add `string? countryCode` to the list handler. Normalize nonblank input and validate its shape without changing contract creation or existing test country-code rules.
4. Start from the existing query and keep its caller/clientId/status conditions. Append a CountryCode predicate when the normalized code exists.
5. Keep ordering and `ToPageAsync` after all filters.
6. Test each country, lowercase/padding, unknown code, blank, invalid shape, and a country/status combination. Use a viewer key to prove the other company's matching contract is absent.
7. Assert the filtered total, then document the parameter and the meaning of employment country.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ContractEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** point to the property being filtered and explain why filtering the Client's headquarters would answer a different question. Show the case where two filters combine to return zero records.

## JS-16: Filter invoices by payroll run

**User story:** As a caller reviewing invoices I can see, I want to list those produced by one payroll run so I can connect a run with its bill.

**Current behavior and small scope:** invoice listing has a clientId filter but no payrollRunId filter. Add optional `Guid? payrollRunId`; do not change invoice creation, amount calculations, or payment handling.

**Open:** [InvoiceEndpoints.cs](../src/Atlas.Api/Endpoints/InvoiceEndpoints.cs), the invoice setup in [PayrollEndpointTests.cs](../tests/Atlas.Tests/Integration/PayrollEndpointTests.cs), and [AuthorizationTests.cs](../tests/Atlas.Tests/Integration/AuthorizationTests.cs).

**Acceptance criteria:**

- `?payrollRunId=<run-id>` returns only visible invoices with that PayrollRunId.
- Omitted means all visible runs. A well-formed unknown GUID, including the empty GUID if no such run exists, returns 200 with an empty array and zero total.
- Malformed GUID text returns the framework's 400 binding response.
- The existing clientId filter, invoice-number ordering, and tenant restriction all remain active.

**Implementation plan:**

1. Use the payroll recipe with one country, two clients, and one active worker contract for each. Start contracts before the two months you will use.
2. Create and complete runs for July and August of the same supported year. Each run should produce one invoice per client; capture the returned IDs.
3. Add `Guid? payrollRunId` to the invoice GET list handler.
4. Retain the existing caller scope and clientId condition. Add a PayrollRunId equality predicate only when supplied.
5. Pass the final query through the existing order and page helper. Do not independently fetch a run and reveal its existence to the caller.
6. As an admin, request July and expect two invoices. As client A, request July and expect only A's July invoice. Combine both filter parameters and verify the same ownership constraint.
7. Test unknown and malformed IDs, omitted filter, and `pageSize=1` with the correct filtered total.
8. Document that an unknown run produces an empty list rather than a detail-route 404.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~PayrollEndpointTests|FullyQualifiedName~AuthorizationTests"`.

**Done / explain:** explain why the new filter narrows existing access instead of authorizing access to a run. Show the response that excludes the other client's invoice.

## JS-17: Filter payroll runs by year and month

**User story:** As a platform admin, I want to find payroll runs for a year or a specific month so I can locate the period I am investigating.

**Current behavior and small scope:** the run list filters country but not period. Add optional `year` and `month`. Use the same supported year interval as payroll creation, 2000 through 2100. This filter does not alter period selection inside payroll calculations.

**Open:** [PayrollEndpoints.cs](../src/Atlas.Api/Endpoints/PayrollEndpoints.cs) and [PayrollEndpointTests.cs](../tests/Atlas.Tests/Integration/PayrollEndpointTests.cs). Compare the list handler with the existing POST year/month validation.

**Acceptance criteria:**

- `?year=2026` returns that year's runs; adding `month=7` selects July.
- Year is 2000–2100 inclusive; month is 1–12 inclusive. A month without a year returns 400 on `year`.
- Invalid numeric ranges return their own field errors. Omitted parameters leave the period unrestricted; country and paging still work.
- Both client roles remain forbidden from this platform-only group.

**Implementation plan:**

1. Create a private country and an active contract starting before July 2026. Create draft runs for July/August 2026 and July 2027; completion is unnecessary.
2. Add `int? year` and `int? month` to the list handler.
3. Before database execution, validate supplied numeric bounds and the month-requires-year rule using a small local errors dictionary.
4. Return `Results.ValidationProblem` if it has errors. Do not silently ignore an orphaned month or substitute the current year.
5. Append Year equality when supplied, then Month equality when supplied, keeping the existing country predicate and `Include`.
6. Preserve Year/Month/CountryCode ordering and page the filtered query.
7. Test all three fixture periods, year-only, year-and-month, invalid boundaries, orphaned month, and invalid text binding. Assert totals for each valid filter.
8. Document defaults and re-run platform authorization tests.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~PayrollEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** explain why `month=7` alone is rejected in this proposed contract instead of searching every July. The API policy should be explicit rather than accidental.

## JS-18: Filter FX rates by effective date range

**User story:** As an authenticated caller reviewing exchange-rate history, I want to choose an effective-date interval so I can inspect only the relevant rate records.

**Current behavior and small scope:** the FX list supports base and quote currency filters, but no date range. Add optional `effectiveFrom` and `effectiveTo` on stored EffectiveDate. This lists historical records; it does not select “the best rate,” calculate conversions, or change payroll's rate-selection method.

**Open:** [FxRateEndpoints.cs](../src/Atlas.Api/Endpoints/FxRateEndpoints.cs), [FxRate.cs](../src/Atlas.Domain/Entities/FxRate.cs), and [FxAndMultiCurrencyTests.cs](../tests/Atlas.Tests/Integration/FxAndMultiCurrencyTests.cs).

**Acceptance criteria:**

- Bounds are inclusive: `effectiveFrom=2026-02-01&effectiveTo=2026-03-01` includes rates on both dates.
- Either bound may be omitted. Equal bounds are valid; a reversed pair returns 400 on `effectiveTo`.
- Malformed calendar-date text returns the framework's 400 binding response.
- Currency filters, ordering, auth, and page totals remain correct. Omitted date bounds preserve the current list.

**Implementation plan:**

1. Seed USD/EUR rates on 2026-01-01, 2026-02-01, and 2026-03-01, plus USD/GBP on February 1. Use positive rate values and distinct pair/date combinations.
2. Add `DateOnly? effectiveFrom` and `DateOnly? effectiveTo` to the GET handler.
3. When both exist and the start is later than the end, return a field-specific validation problem before executing the query.
4. Keep existing base/quote normalization. Append `EffectiveDate >= start` and `EffectiveDate <= end` predicates only for supplied bounds.
5. Preserve the existing base/quote/effective-date order and apply pagination afterward.
6. Test both bounds, start-only, end-only, exact one-day range, reversed dates, and a date interval with no matches.
7. Combine the February-through-March range with `baseCurrency=USD&quoteCurrency=EUR`; expect two matching records, including both boundaries.
8. Verify filtered total headers and update the FX-list reference. Do not modify `FxRate.EffectiveForMonth`.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~FxAndMultiCurrencyTests`. Suggested test name: `ListFxRates_EffectiveRange_IncludesBothBoundaries`.

**Done / explain:** explain the difference between returning every record in a date range and finding the single rate effective for a payroll period.

## JS-19: Show only required onboarding items

**User story:** As a client user reviewing a contract's onboarding, I want an option to see only required items so I can focus on the tasks that block activation.

**Current behavior and small scope:** the checklist GET returns all items plus `IsComplete`. Add optional `requiredOnly`; preserve the response record and calculate completion from the complete checklist. This story does not complete items or change what activation requires.

**Open:** [OnboardingEndpoints.cs](../src/Atlas.Api/Endpoints/OnboardingEndpoints.cs), [OnboardingItem.cs](../src/Atlas.Domain/Entities/OnboardingItem.cs), and [OnboardingEndpointTests.cs](../tests/Atlas.Tests/Integration/OnboardingEndpointTests.cs).

**Acceptance criteria:**

- `?requiredOnly=true` returns the four required items in the default five-item checklist.
- Omitted or false returns all five. `IsComplete` has the same meaning and value with either view of the same checklist.
- Completing all required items makes IsComplete true even if the optional tax item is incomplete.
- Own-client viewers may read the view. Another client's contract or an unknown contract returns 404. Invalid Boolean text returns 400 binding failure.

**Implementation plan:**

1. Use the draft-contract recipe, then fetch its checklist. Record the default required/optional counts.
2. Add `bool? requiredOnly` to the GET handler after identifying its current contract visibility guard.
3. Leave authorization and the query loading the full bounded checklist unchanged for this small story.
4. Calculate the current IsComplete value from that full list before selecting which items to return.
5. Build the response Items from required entries only when `requiredOnly` is true; otherwise use every item. Preserve the current type ordering.
6. Test a fresh checklist through both views. Complete its required items via the existing endpoint and repeat both views, checking the same IsComplete value.
7. Test client viewer access and cross-client 404 with the flag present. Check that no extra completion or save operation was added to GET.
8. Document that the filter changes the displayed items, not the underlying completion rule.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~OnboardingEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** show a response with four items and the unchanged summary. Explain why display filtering and business completion are separate decisions.

## JS-20: Filter a worker's documents by type

**User story:** As a caller viewing a worker I can access, I want to list one kind of compliance document so I can find passports or permits without scanning every document.

**Current behavior and small scope:** the nested document list has no type filter. Add optional `type`, using a defined ComplianceDocumentType. Preserve names and defined numeric strings just as JS-06 specifies; JS-06 itself need not be implemented first.

**Open:** [ComplianceEndpoints.cs](../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs), [ComplianceDocument.cs](../src/Atlas.Domain/Entities/ComplianceDocument.cs), and [ComplianceEndpointTests.cs](../tests/Atlas.Tests/Integration/ComplianceEndpointTests.cs).

**Acceptance criteria:**

- `?type=Passport`, lowercase `passport`, and `type=0` return only this worker's passports.
- Omitted or blank means all document types. Unsupported text and undefined numeric values return 400 on `type` for a visible worker.
- A valid type with no matches returns 200 with an empty array.
- Missing or invisible workers retain the existing 404 behavior. No pagination is added to this bounded sub-resource list.

**Implementation plan:**

1. Create two workers. For worker A, add two Passports and one WorkPermit; for worker B, add another Passport.
2. Add `string? type` to the document-list handler, keeping its worker existence and caller-visibility checks first.
3. After those checks, normalize a supplied nonblank type, parse it case-insensitively, and require `Enum.IsDefined`. Return `errors.type` if invalid.
4. Turn the existing worker-scoped document query into a query variable and append a Type equality predicate only for a supplied valid filter.
5. Keep the worker ID restriction, expiry ordering, and current response mapping/status computation. Materialize after filtering.
6. Test the two passports, one permit, an empty valid type, missing filter, numeric compatibility, and invalid values.
7. Give worker A a contract for client A and worker B one for client B. Verify a client A viewer can read A's filtered list but gets 404 for B.
8. Document the filter and confirm the result contains no worker B document even in an admin request to worker A's route.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ComplianceEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** identify both restrictions in the query: parent worker and document type. Explain why filtering by type must not replace parent scoping.

## JS-21: Fetch one worker document

**User story:** As a caller viewing a worker I can access, I want to fetch a single compliance document by its URL so I can reopen the resource after creation.

**Current behavior and small scope:** POST already returns a Location shaped like `/api/workers/{workerId}/documents/{documentId}`, but there is no matching detail GET in the current endpoint file. Add that GET under the existing workerDocs group. Return the existing `ComplianceDocumentResponse`; do not add file downloads, uploads, or update/delete operations.

**Open:** [ComplianceEndpoints.cs](../src/Atlas.Api/Endpoints/ComplianceEndpoints.cs), the worker visibility pattern in [WorkerEndpoints.cs](../src/Atlas.Api/Endpoints/WorkerEndpoints.cs), and [ComplianceEndpointTests.cs](../tests/Atlas.Tests/Integration/ComplianceEndpointTests.cs).

**Acceptance criteria:**

- Following the Location from a successful document POST returns 200 and that document's ID, worker ID, name, and existing computed-status shape.
- An unknown document returns 404. A real document under the wrong worker ID also returns 404.
- Platform admins and users who can view the parent worker can read it; unrelated client users receive 404 and anonymous callers receive 401.
- GET does not create, update, or delete rows.

**Implementation plan:**

1. Add an integration test that creates a worker and document, captures `response.Headers.Location`, then GETs that URI. Use a no-expiry document to keep the initial expected status simple.
2. Register `workerDocs.MapGet("/{documentId:guid}", ...)` with workerId, documentId, ClaimsPrincipal, and AtlasDbContext parameters.
3. Follow the existing document-list parent existence and worker-visibility checks. Reuse that short pattern locally; a repository-wide auth refactor is not required.
4. Find the document with a query requiring both `Id == documentId` and `WorkerId == workerId`. An ID-only lookup would allow a mismatched nested URL.
5. Return NotFound for no match; otherwise use the existing `ToResponse` helper with the current UTC date and return Ok.
6. Test an unknown document, another worker's document ID under this worker, an unknown worker, a permitted viewer, an unrelated client, and no key.
7. Compare the detail fields with the POST/list shape and verify no save call appears in the new handler.
8. Add the detail route to the API reference. Existing Program mapping already calls `MapComplianceEndpoints`, so no new top-level route registration is needed.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ComplianceEndpointTests|FullyQualifiedName~AuthorizationMatrixTests"`.

**Done / explain:** demonstrate the POST → Location → GET sequence. Explain why the query must check both route identifiers.

## JS-22: Filter salary history through a date

**User story:** As a caller reviewing a contract I can see, I want to hide salary-history entries effective after a selected date so I can focus on terms up to that date.

**Current behavior and small scope:** the salary-history route returns all records for a visible contract, ordered by effective date and creation time. Add optional `effectiveOnOrBefore`. Return every qualifying entry, not only the latest one. This is a view over current history, not a reconstruction of what the system knew on that day.

**Open:** the salary-history handler in [AmendmentEndpoints.cs](../src/Atlas.Api/Endpoints/AmendmentEndpoints.cs), [SalaryRecord.cs](../src/Atlas.Domain/Entities/SalaryRecord.cs), and [AmendmentEndpointTests.cs](../tests/Atlas.Tests/Integration/AmendmentEndpointTests.cs).

**Acceptance criteria:**

- Given history effective January 1, June 1, and September 1 of 2026, `effectiveOnOrBefore=2026-06-01` returns January and June.
- Omitted means all history; a date before the first record returns an empty array.
- The exact boundary is included. Malformed date text returns 400 binding failure.
- Contract visibility and 404 rules remain intact. No salary record, contract term, or payslip is modified.

**Implementation plan:**

1. Use the amendment test's contract setup and approved-amendment pattern to create the three effective-date records. Capture their IDs; use one contract for all three.
2. Add `DateOnly? effectiveOnOrBefore` to the salary-history GET signature.
3. Leave its existing contract load and `CanViewClient` guard unchanged.
4. Build the SalaryRecords query starting with the contract ID restriction. Add an EffectiveDate less-than-or-equal predicate when supplied.
5. Preserve `OrderBy(EffectiveDate).ThenBy(CreatedAtUtc)` and the existing response mapping after materialization.
6. Test the June boundary, a before-first date, after-last, and omission. Assert the exact qualifying record IDs and unchanged stored record count.
7. Add another client's contract/history and verify a scoped caller still gets 404 for it, including when a cutoff is supplied.
8. Document the parameter and clarify that it does not call or change `SalaryRecord.EffectiveForMonth`.

**Verify:** `dotnet test Atlas.slnx --filter FullyQualifiedName~AmendmentEndpointTests`. Suggested test name: `SalaryHistory_CutoffIncludesBoundaryAndKeepsEarlierRecords`.

**Done / explain:** explain why selecting all records through a date is different from calculating one month's payable salary. Show that the filter did not delete the September record.

## JS-23: Filter compliance expiry reports by country

**User story:** As a platform admin reviewing compliance deadlines, I want to restrict the expiry report to a worker country so I can focus on one location.

**Current behavior and small scope:** `/api/reports/compliance-expiries` accepts asOf and withinDays, but no country. Add optional countryCode to this report only. Do not also change `/api/compliance/expiring`, document status logic, or document creation in this story.

**Open:** [ReportEndpoints.cs](../src/Atlas.Api/Endpoints/ReportEndpoints.cs), `ComplianceExpiriesAsync` in [ReportService.cs](../src/Atlas.Api/Reporting/ReportService.cs), and [ReportEndpointTests.cs](../tests/Atlas.Tests/Integration/ReportEndpointTests.cs).

**Acceptance criteria:**

- `countryCode=PH` returns only documents whose worker has CountryCode PH, while preserving the date window.
- Trim/uppercase the country; omitted/blank means all countries. Invalid nonblank two-letter ASCII shape returns 400 on `countryCode`; a well-shaped unknown code returns an empty array.
- Already-expired records remain included according to the existing report policy. Documents without expiry stay excluded.
- The report stays platform-only and uses its existing unpaged row shape.

**Implementation plan:**

1. Use a fixed explicit `asOf=2026-06-01` and `withinDays=30`. Create a PH worker and a US worker, each with a document expiring June 15; give PH an additional expired document and a no-expiry document.
2. Add `string? countryCode` to the report endpoint and normalize/validate it. Preserve existing window validation and default date resolution.
3. Add a nullable country argument to `ComplianceExpiriesAsync`. Find all its call sites with `rg -n 'ComplianceExpiriesAsync' src tests` and update each to pass the intended value.
4. In the service, build a query variable and conditionally restrict `d.Worker.CountryCode` before materializing it. Keep the existing expiry predicate, Include, no-tracking behavior, ordering, and cancellation token.
5. Keep the row mapping unchanged. Do not filter the already-built response after the service returns.
6. Test PH-only, US-only, omitted, normalized input, unknown, and invalid shape. For PH, assert inclusion of the expired and June 15 records and exclusion of the no-expiry record.
7. Re-run client-role rejection and date-window tests, then document this route's new parameter.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ReportEndpointTests|FullyQualifiedName~ReportQueryTests"`.

**Done / explain:** trace the country value from the HTTP parameter into the service query. Explain why the source is the worker's country and why the old date rules still apply.

## JS-24: Filter headcount reports by country

**User story:** As a platform admin, I want headcount for one employment country so its total and company breakdown describe the same selected population.

**Current behavior and small scope:** headcount accepts an asOf date but aggregates all countries. Add optional countryCode and apply it before any counts or grouping. Preserve the current definition of employed contracts and the existing response fields.

**Open:** the headcount route in [ReportEndpoints.cs](../src/Atlas.Api/Endpoints/ReportEndpoints.cs), `HeadcountAsync` in [ReportService.cs](../src/Atlas.Api/Reporting/ReportService.cs), and the headcount setup in [ReportEndpointTests.cs](../tests/Atlas.Tests/Integration/ReportEndpointTests.cs).

**Acceptance criteria:**

- For two qualifying PH contracts and one qualifying US contract on 2026-06-15, filtering PH produces Total 2, a PH country count of 2, and client counts summing to 2.
- A Draft contract in PH does not count. Existing start/end-date rules continue to determine employment on the selected date.
- Omitted/blank country means all countries; trim and uppercase a supplied value. Invalid two-ASCII-letter shape returns 400; unknown valid shape yields Total 0 and empty breakdown arrays.
- The endpoint stays platform-only; existing JSON fields and asOf default behavior remain intact.

**Implementation plan:**

1. Use the existing headcount test helpers to create the three active contracts above, spread across two clients. Add a PH Draft contract to distinguish country matching from employment eligibility.
2. Add and validate `string? countryCode` at the endpoint, leaving the explicit/default asOf resolution in place.
3. Pass a nullable normalized country into `HeadcountAsync`; search for every method call before changing the signature.
4. Start from the existing employed-contract query. Add a CountryCode predicate before its projection and `ToListAsync`.
5. Keep the subsequent total, country grouping, client-name lookup, and client grouping based on that same filtered employed collection.
6. Do not filter only `ByCountry` after calculating a global Total; that would make the response internally inconsistent.
7. Test PH, US, omitted, unknown, invalid shape, and a combined country/asOf date before the contracts started. Check Total and both arrays together.
8. Re-run the existing terminated-contract date cases and platform-role tests. Document that every breakdown reflects the selected country.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ReportEndpointTests|FullyQualifiedName~ReportQueryTests"`.

**Done / explain:** point to the single filtered population used for all three output calculations. Show why a country row count without a matching total would be misleading.

## JS-25: Filter payroll cost reports by run status

**User story:** As a platform admin reviewing payroll costs, I want to choose draft or completed runs so I can separate provisional run costs from completed-run costs.

**Current behavior and small scope:** the cost report includes payslips from both statuses. Add optional `runStatus`, filtering on `PayrollRun.Status` before grouping. Keep all existing amounts and grouping rules; do not change payroll completion, invoice generation, or whether a payslip is included in a run.

**Open:** [ReportEndpoints.cs](../src/Atlas.Api/Endpoints/ReportEndpoints.cs), `PayrollCostsAsync` in [ReportService.cs](../src/Atlas.Api/Reporting/ReportService.cs), [PayrollRun.cs](../src/Atlas.Domain/Entities/PayrollRun.cs), and [ReportEndpointTests.cs](../tests/Atlas.Tests/Integration/ReportEndpointTests.cs).

**Acceptance criteria:**

- `runStatus=Draft` selects draft-run payslips; `Completed` selects completed-run payslips. Names are case-insensitive; defined numeric strings `0` and `1` keep the enum-input policy used in these stories.
- Missing/blank means both statuses. Unsupported text or undefined numbers return 400 on `runStatus`.
- Country, client, and period filters still combine, and currencies remain separate.
- Report rows and monetary calculations have their current shape and meaning. No stored status or amount changes because the report was read.

**Implementation plan:**

1. In a fresh fixture, create a private USD country with zero employer/deduction rates and one active contract with salary 1,000, no benefits/expenses, starting before July 2026. Use a USD-billed client.
2. Create July and August runs. Keep July Draft and complete August. Each payslip has gross and total cost 1,000 under this fixture; invoice fees are not part of this report's TotalCost.
3. Add `string? runStatus` to the endpoint. For nonblank input, trim it, parse PayrollRunStatus case-insensitively, and require a defined value. Return the existing validation-problem style on failure.
4. Pass the nullable parsed enum to `PayrollCostsAsync`; search and update all call sites. Keep period validation and integer-bound construction as they are.
5. Add a predicate on `p.PayrollRun.Status` to the service query only when supplied, before scalar projection and materialization.
6. Keep the current decimal sums and year/month/currency grouping. Do not re-query or recalculate payroll to determine completion status.
7. Expect July only for Draft, August only for Completed, and both rows when omitted. Assert exact months, counts, currency, gross, and total cost. Test lowercase, numeric compatibility, invalid values, and a combined period/status request with no matches.
8. Confirm both client roles remain forbidden. Fetch the run details again to prove reading reports did not change their statuses; update the cost-report API section.

**Verify:** `dotnet test Atlas.slnx --filter "FullyQualifiedName~ReportEndpointTests|FullyQualifiedName~ReportQueryTests|FullyQualifiedName~PayrollEndpointTests"`.

**Done / explain:** explain why the filter belongs before aggregation and why “completed payroll costs” is still different from “paid invoices.” Atlas does not yet track invoice payments.

---

## Your repeatable handoff checklist

Use this for each story, not as a requirement to finish all 25 together:

- [ ] I can explain the user-visible problem in one sentence.
- [ ] I found the current route, field, or query before editing.
- [ ] I implemented only this story's agreed scope.
- [ ] My examples include a success, a boundary/empty case, and a relevant failure.
- [ ] My tests verify returned values or stored state, not only the HTTP status.
- [ ] I preserved the existing role and client-visibility rules.
- [ ] I ran the focused command and recorded the actual result.
- [ ] I ran the broader suite before handoff and investigated any failures.
- [ ] I updated the affected API-reference section.
- [ ] I reviewed my diff and can explain one limitation.

Suggested review note:

```text
Story ID:
Problem and example:
Changed behavior:
Files changed:
Acceptance criteria demonstrated:
Commands and actual results:
Authorization check:
One limitation:
One question for my reviewer:
```

If you get stuck, use [the pairing guide](15-pairing-guide.md): state your prediction, what you observed, and the exact step that is unclear. Ask for one hint before a full solution. The goal is for you to implement and understand these changes yourself.
