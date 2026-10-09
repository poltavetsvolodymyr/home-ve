# Security

home-ve controls virtual machines and runs parts of itself as root, so security reports are welcome and taken
seriously.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately instead: on GitHub, open the repository's **Security** tab
and choose **Report a vulnerability**. Only the maintainer sees the report.

Helpful to include: what an attacker can do, the steps or a proof of concept, and the commit you tested.

You will get an answer within a week. Once a fix is out, the report can be made public, with credit to you if you
want it.

## What is supported

The latest release (the `stable` branch, which hosts follow by default) and `main` (the dev channel). Fixes land
on `main` first, then in a release as soon as possible.

## Scope

Anything in this repository: the backend and its API, the web UI, the scripts that run as root (`deploy/vm/`,
`deploy/offsite/`, `deploy/install.sh`), the polkit rule and the systemd units. The web UI is meant for your own
network: it lets in only private networks by default and asks for a password. Any way around either of these is a
vulnerability.
