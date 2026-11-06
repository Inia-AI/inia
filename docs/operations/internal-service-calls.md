---
type: runbook
status: current
description: "The process a deployment follows before a workflow may call an internal service: request, review, approval, the change, verification and the record of every exception. Read when a workflow's Web API Call or an email link is refused with \"Refused to connect\", or someone asks to let workflows reach a service inside a deployment's network."
---

# Calling an Internal Service from a Workflow

**On this page:** Why the check exists · What a refused call looks like · The process · Rules for an entry · Where the entry goes

A workflow cannot call a service inside a deployment's own network. The Web API Call block and the email link downloader refuse every internal address. A deployment gets an exception only through the process on this page.

> [!CAUTION]
> **One entry opens a service to everyone on the deployment**
>
> An exception is an entry in the deployment setting `PlatformSettings:OutboundAccess:AllowedInternalHosts`. The entry opens the service to every workflow of every organization on that deployment, and to every link in every email it processes. It is not limited to the workflow that asked for it. Never add an entry outside this process.

## Why the check exists

The URL of a Web API Call can come from a workflow author, from a model answer, from an earlier block's output, or from a run-time variable that any signed-in user sends. The links that the email downloader fetches come from whoever sent the email. Without the check, any of them can point the platform at its own network:

- The cloud metadata service hands out the host's credentials, which can read the deployment's secrets.
- Databases, file storage, the agent runtime and admin interfaces answer on internal addresses.


The check judges the address that each host name resolves to, on the first request, every redirect, every poll and the completion URL. A numeric address form, a redirect or another name for the same host gets the same answer. The refused ranges are listed in the [Web API Call reference](../guides/blocks/block-reference.md) and in [Application Configuration](application-configuration.md#outboundaccess).

## What a refused call looks like

The block fails at once, with no retry:

> Refused to connect: reports.internal resolves to a private address. A workflow reaches an internal service only through an approved exception: see "Calling an Internal Service from a Workflow" in the platform documentation.

For a host name, the message leaves out the internal address, so that a workflow author cannot map internal names. The server log keeps it. A refused email link is skipped like a failed download, and the log names the rule.

If you build workflows and see this message, do not try another form of the address. Ask the engineering team, who start the process below.

## The process

1. **Request.** Open a ticket with the team that runs the deployment, titled "Allow workflows on `<deployment>` to call `<service>`". It holds:
   - the service and the team that owns it;
   - the host name, or the address when the service has no stable name;
   - the deployment, and the organizations and workflows that need the call;
   - what the call sends and what it receives;
   - how the service authenticates its callers.
2. **Review.** The platform owner checks the request against [the rules](#rules-for-an-entry). A request that breaks a rule is refused, with the reason in the ticket.
3. **Approval.** The platform owner approves in the ticket. The approval names the exact entry.
4. **Change.** The entry goes into the deployment's configuration through [its own channel](#where-the-entry-goes). Nobody adds an entry by hand on a host or in a container.
5. **Verification.** Run the workflow on that deployment. The call to the approved service succeeds, and a call to any other internal host is still refused.
6. **Record.** Keep a record of the entry with its ticket, its approver and its date.

To remove an entry, follow the same steps. An entry that nothing uses any more is removed.


### Done when

- The approved workflow's call to the service succeeds on that deployment.
- A Web API Call to another internal host on the same deployment still fails with `Refused to connect: …`.
- The record of entries has the entry, its ticket, its approver and its date.

## Rules for an entry

- **Never a cloud metadata service:** `169.254.169.254`, `fd00:ec2::254` or `168.63.129.16`. The platform refuses to start with an entry that covers one, and refuses one even when an allowed host name resolves to it.
- **Never the platform's own services:** its database, its file storage, the agent runtime, the platform API or an admin interface.
- **Prefer one host name.** An entry for a name allows every address that the name resolves to. Use an address, or the smallest range, only when the service has no stable name. A wide range such as `10.0.0.0/8` is not an acceptable entry.
- **The service authenticates its callers,** so a workflow without the credential gets nothing from it.
- **One deployment per entry.** Each deployment needs its own ticket.

## Where the entry goes

The setting takes one entry per indexed key (`__0`, `__1` and so on). As an environment value:

```
PlatformSettings__OutboundAccess__AllowedInternalHosts__0=reports.internal
```

An entry that is not a host name, an IP address or a CIDR range stops startup, and so does a list set as one value.

On an install that its operator runs, the entry goes into the operator's own configuration, and the operator applies it.

