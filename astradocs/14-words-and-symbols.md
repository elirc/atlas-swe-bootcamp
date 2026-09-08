# Words and symbols you can look up while reading

You do not need to memorize this page. Find the word that blocks the current sentence, then return to that sentence.

## The app's words

| Word | Plain meaning here | Example |
| --- | --- | --- |
| Backend | Program receiving requests and working with data | Atlas.Api |
| API | The operations callers can request | GET a country; POST a contract activation |
| Endpoint | Handler associated with an HTTP method and route | GET `/api/countries/{code}` |
| Route | URL pattern matched by the app | `/api/countries/{code}` |
| Request | Incoming message | Method, URL, headers, and possibly JSON |
| Response | Outgoing result | Status, headers, and possibly JSON |
| Header | Metadata outside the JSON body | `X-Api-Key`, `X-Request-Id` |
| JSON | A text format for structured values | A country response with named fields |
| Entity | A model with identity and persisted data | Country or EmploymentContract |
| DTO / response model | Values selected for input or output | CountryResponse |
| Domain | Business concepts and rules | Contract states and payroll calculations |
| Invariant | A condition valid behavior must preserve | The calculator's net + deductions = gross |
| State transition | A permitted change in lifecycle | Draft to Active |
| Tenant / client scope | Which company's data a caller may access | A client user sees its own invoices |
| Orchestration | Coordinating several operations | PayrollService loads inputs and builds payslips |

## Storage and testing words

| Word | Plain meaning here | Common confusion |
| --- | --- | --- |
| SQLite | The database engine used by this project | It is not the same thing as a C# list |
| EF Core | Library connecting C# entity/query code with the database | It does not remove the need to understand queries |
| DbContext | Object used to query and track entity changes | It is not itself the persisted database |
| DbSet | Access to entities of a given type through EF | `db.Countries` is not all countries already loaded |
| Primary key | Value identifying a record | Country uses `Code`; many other entities use `Id` |
| Foreign key | Value connecting a record to another record | Contract `ClientId` refers to a client |
| Query | A request to retrieve/filter data | Constructing a query is not always executing it |
| Tracking | EF remembering entity state and changes | Tracking a change is not saving it |
| Dependency injection | Framework supplies configured dependencies | A handler's DbContext is not sent in JSON |
| Unit test | Directly exercises a small behavior | A calculator test needs no running API |
| Integration test | Exercises connected components | Atlas tests can use HTTP and real SQLite |
| Assertion | Check of an expected outcome | `Assert.Equal` |
| Regression test | Protects against a defect returning | Reject an invalid input that used to pass |
| Fixture | Setup shared by a group of tests | Sharing a factory can mean sharing its database |

## C# symbols in these guides

| Syntax | Meaning in the shown usage |
| --- | --- |
| `string?`, `DateOnly?` | Value may be absent/null |
| `var x = ...` | Compiler infers the local variable's type |
| `required` | C# construction requires initialization; does not ensure valid business content |
| `100m` | Decimal literal, used for money/rates |
| `==`, `!=` | Equal / not equal |
| `=` | Assignment, such as changing Status |
| `&&`, `||` | AND / OR conditions |
| `!condition` | Negate a Boolean condition |
| `value!` | Null-forgiving annotation; does not check or change the runtime value |
| `condition ? a : b` | Choose a value based on a condition |
| `value ?? fallback` | Use the fallback when value is null |
| `c => c.Code` | Small function selecting a country's Code |
| `await` | Await completion before continuing this part of the method |
| `return` | Finish this method/handler with its result |
| `throw` | Interrupt the normal path with an exception |

Same symbols can appear in other language constructs. Translate them in the context of the line you are reading. For a full slow example, return to [page 06](06-read-code-slowly.md).
