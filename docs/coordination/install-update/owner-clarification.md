# Owner clarification: GitHub installation

Source: follow-up owner messages in the same session, after local delivery. Date/time not supplied in the messages.

Exact owner words:
- "ehy use a new cli command? why not have the install script be dowloaded ffrom github??"
- "whats this archive thing? who asked for archives?"
- "so now i have the code on github be cant just run curl ..../install.sh | bash ..."
- In response to the explicit question "Should the one-line installer build from the code on GitHub, or download prebuilt binaries from GitHub Releases?": "releases"

Accepted interpretation: installation must support a one-line GitHub-hosted install.sh entry point using prebuilt GitHub Releases; the owner should not manually build, copy, or select a package archive for normal installation/update. Archives may remain internal release artifacts and an explicit offline path, but they are not a required owner workflow. Prefer reuse of the existing transactional deployment engine, preservation behavior and rollback.

This supersedes the original prepared assignment's local-archive-only baseline for user-facing onboarding and updates. It does not revoke configuration/release preservation, isolated verification, independent review, local delivery target, no push/publication, no privileged host/Pi installation, or unverified physical acceptance. Changes to release automation may be committed locally; publishing assets or running a remote workflow is not authorized.
