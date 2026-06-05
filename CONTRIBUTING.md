# Contributing to Noctra

Noctra is proprietary software owned by Kynora Studio. This repository is not
an open-source contribution project.

## Authorization

Code, documentation, translation, design, and testing contributions are
accepted only from Kynora Studio personnel or contributors who have received
prior written authorization.

Do not fork, copy, modify, publish, or redistribute the repository unless a
written agreement with Kynora Studio expressly permits it. Access to the
repository does not grant a software license.

Authorized contributors must ensure they have the right to submit their work
and that Kynora Studio may use it under the applicable contributor, employment,
or contractor agreement. Do not submit third-party code or assets without
compatible written permission and required notices.

## Development Requirements

- Follow the existing architecture and `.editorconfig`.
- Keep UI concerns out of `Noctra.Core`.
- Add focused tests for behavioral changes and regressions.
- Run `dotnet test .\Noctra.Tests\Noctra.Tests.csproj` before review.
- Do not commit credentials, provider accounts, playlist URLs, tokens, personal
  data, production secrets, or private signing material.
- Keep user data intact during provider refresh and migration work.
- Document user-visible changes in `CHANGELOG.md`.

## Review Workflow

Authorized contributors should work in a Kynora Studio-approved branch and
submit changes through the review process designated by the maintainer. A
submission may be rejected, revised, or incorporated at Kynora Studio's
discretion.

## Bug and Security Reports

General bug reports should include reproducible steps, expected and actual
behavior, the Noctra version, and sanitized logs where relevant.

Security vulnerabilities must be reported privately according to
[`SECURITY.md`](./SECURITY.md).

Licensing and contribution inquiries: **kynora.studio@gmail.com**
