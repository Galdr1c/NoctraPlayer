# Metadata HTTP lifecycle design

## Scope

Close report items P1-25 and P1-26 without changing the three-minute shared `HttpClient` policy
used by large playlist/provider downloads.

## Design

- `MetadataService` owns every `HttpResponseMessage` returned by its fallback helper and disposes
  it after JSON consumption.
- Failed direct-fallback responses are disposed inside the helper because they are not returned.
- Each metadata HTTP attempt gets a linked cancellation source with a 20-second timeout.
- Caller cancellation remains distinguishable: caller-triggered cancellation is rethrown, while a
  metadata-attempt timeout may enter the existing proxy-to-direct fallback path.
- Proxy and direct attempts receive independent 20-second budgets.

## Verification

- A tracking HTTP response proves response/content disposal after deserialization.
- A blocking handler proves a metadata attempt is cancelled by the short timeout without altering
  the shared client's timeout.
- Existing staged caller-cancellation tests must continue to pass.

## Non-goals

- Replacing the whole application with `IHttpClientFactory`.
- Shortening playlist, Xtream, Stalker, EPG or download request timeouts.
