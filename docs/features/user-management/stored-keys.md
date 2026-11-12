---
type: explanation
status: current
description: "Explains stored keys: per-organization API keys that a Web API Call header names with {StoredKey.Name}, so the key stays out of the workflow, its run records and its downloads. Read when you give a workflow a third-party API key, rotate one, or debug a web API call that fails on a missing or unreadable key."
---

# Stored Keys

A stored key is a credential, such as a third-party API key, that an organization keeps in the platform for its workflows. A [Web API Call block](../../guides/blocks/block-reference.md#web-api-call) names the key in a request header with `{StoredKey.Name}`, and the platform fills in the value when it sends the request. The workflow template, each run record, a downloaded `.workflow` file and a shared component hold only the placeholder.

## Why a typed key is a problem

A key typed into a header value is part of the workflow template. Each run copies the template into its run record with the header values it sent, and the editor shows them under **Header Value After Execution**. A `.workflow` download, the component library and every database copy carry the key too. A stored key reduces all of these to a placeholder.

## Manage the keys

Open **Administration** and the **Stored keys** tab. The page admits `SuperAdmin` and `Admin` (`Roles.ADMIN_ROLES`). The tab lists the keys of the organization that the tenant selector shows, with the name and the date of the last change. A value never shows again after it is saved.

- **Add Key** opens a dialog with a name and a value. A name starts with a letter and holds letters, digits, dashes and underscores, up to 100 characters. A value holds up to 1,000 characters.
- **Replace Value** sets a new value for a key. The next request of every workflow that names the key uses the new value.
- **Delete Key** deletes the key after a confirmation. The row leaves the database, and the name is free to add again. A workflow that names the key fails at its web API call until someone adds a key with the same name.

The admin app writes the keys through the application commands `SetStoredKeyCommand` and `DeleteStoredKeyCommand`. No v1 API endpoint reads or writes them.

## Use a key in a block

A header value names a stored key with `{StoredKey.Name}`, alone or inside other text:

```json
"AdditionalHeaders": {
  "x-api-key": "{StoredKey.ParallelApiKey}",
  "Authorization": "Bearer {StoredKey.PerplexityApiKey}"
}
```

In the block editor, **Insert stored key** under each header value lists the organization's key names and adds the placeholder of the one you choose. When the header is `Authorization`, `X-API-Key` or `Api-Key` (`WebApiCallHostGate.CredentialHeaderNames`, ignoring case) and its value holds no stored-key placeholder, the row shows the warning `This header carries a credential. Add it under Administration, Stored keys, and insert it here, so it stays out of the workflow and its run records.` The workflow still saves.

`StoredKey` is a reserved tag, so the editor's unresolved-tag warning and the dependency graph leave these placeholders alone.

## The keys tenants had before

Tenants had three API key fields, Parallel, Perplexity and FMP, on the tenant dialog. A header named them with `{Organization.ParallelApiKey}`, `{Organization.PerplexityApiKey}` and `{Organization.FmpApiKey}`. The run filled those values into the block's run record, and the fields were not encrypted.

The fields left the tenant dialog. At startup, the platform moves each tenant's value into a stored key of the same name, `ParallelApiKey`, `PerplexityApiKey` or `FmpApiKey`, and empties the field. A stored key that already has the name keeps its value. The three placeholders keep working and now name those stored keys, so `{Organization.PerplexityApiKey}` is the same as `{StoredKey.PerplexityApiKey}`. The columns stay on the table until a later release removes them.

## At run time

The block reads the key of the organization that the run belongs to. Templates are shared across organizations, so a workflow that names `{StoredKey.ParallelApiKey}` uses the `ParallelApiKey` key of each organization that runs it.

When the organization has no key by that name, the block uses the deployment's own value for it, `PlatformSettings:StoredKeys:DeploymentKeys:<name>`, when the deployment sets one. A deployment whose customers bring no keys of their own sets one value per provider. The organization's own key always wins, and a key the key ring cannot decrypt still fails. The deployment's values are secrets: set them as secret settings, never in a committed file ([Application Configuration](../../operations/application-configuration.md#storedkeys)). A workflow editor of any organization on the deployment can send a deployment value to any host a WebApiCall block names, so set one only where every workflow editor may see it. Every organization that uses a deployment value also shares one request limit per host, because the limit counts requests per host and key.

- The run leaves stored-key placeholders in the block, so its execution state and **Header Value After Execution** show `{StoredKey.ParallelApiKey}`, never the value.
- The value goes only into the outgoing request, including each poll and the completion URL. The request-header trace log masks every header value. A log line or a failure text that names the request's address leaves out its query, its fragment and its user-info (`LogSanitizer.ForLogUrl`), so a key placed in the URL stays out of both.
- Only header values take stored keys. A stored-key placeholder in a URL, a payload or a prompt stays as written.
- When neither the organization nor the deployment has a key by that name, the block fails before it sends a request: `This organization has no stored key named 'ParallelApiKey'. An admin adds it under Administration, Stored keys.`
- When the key ring cannot decrypt the value, the block fails before it sends a request: `The stored key 'ParallelApiKey' cannot be decrypted with this deployment's key ring. An admin sets its value again under Administration, Stored keys.`

## Storage and encryption

The keys live in `Core.OrganizationStoredKeys`, one row per organization and name. `ProtectedValue` holds the value encrypted by ASP.NET Core Data Protection, with the purpose `IniaAi.StoredKeys.v1`. The admin app encrypts and the API, which runs the workflows, decrypts, so both apps must use one key ring.

Both apps use the key ring in the UserData database today: the admin app always does, and the API does with its default `DataProtection:Provider` of `database`. Two limits follow:

- A copy of both databases holds the encrypted keys and the key ring together, so the app can decrypt the keys from that copy.
- A deployment that moves the API's key ring to AWS or Azure with `DataProtection:Provider` breaks stored keys, because the admin app keeps its ring in the database. Move both apps together.

Run records written before stored keys still hold the values that the run filled in at the time.
