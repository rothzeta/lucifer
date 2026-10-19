# Owner authorization: publish master and v0.1.0

Source: immediately preceding assistant question and owner reply in this session; owner message timestamp was not supplied.

Assistant question, exact words:
"May I push the reviewed code to `master` and publish release `v0.1.0`?"

Assistant explanation, exact words:
"Your earlier instruction explicitly said “Do not push,” so publication needs your authorization."

Owner response, exact words:
"yes"

Accepted interpretation: authorize publishing the already reviewed install/update correction to the configured GitHub repository's master branch and publishing the first GitHub Release v0.1.0 with matching binaries/checksums/installer assets, including triggering and monitoring the release workflow. This supersedes the earlier no-push/no-publication restriction for this named target/version only. It does not authorize force pushing, moving an existing tag, changing unrelated refs/settings, approving native security dialogs, or a real privileged installation on this host or a live Pi. Existing preservation, review and physical-acceptance limits continue.

Published code must match the accepted product; destination movement or a required material fix must be resolved through combined verification and independent review before further code publication. Keep the retained owner checkout and unrelated source work intact.
