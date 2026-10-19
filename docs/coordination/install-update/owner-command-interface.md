# Owner decision: installation and update interface

Source: direct messages in this API session; timestamps not supplied.

Owner, exact words: "why is there a separate lucifer install application???"
Assistant proposed, exact words: "The interface should simply be GitHub’s `install.sh` for installation and `lucifer update` for updates."
Owner accepted, exact words: "yes it should"

DEC-008: implement the accepted interface. GitHub-hosted install.sh remains the installation entry point; the installed Lucifer command exposes update as `lucifer update`. Remove the separately exposed lucifer-install tool as the normal interface. Reuse private transactional implementation where useful; do not add a second product application. Preserve configuration, previous releases, rollback, and isolated verification. Minimal command dispatch/packaging changes are in scope; stopped playback/shutdown fixes remain out of scope and separate. Deliver reviewed local commits to ruach/lucifer-install-update under continuing original authority. Existing named v0.1.0 tag is immutable; this correction does not grant tag replacement, new-version publication, failed-check bypass or live privileged installation.
