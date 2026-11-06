---
type: reference
status: current
description: "Catalogs which external services receive customer content, per deployment mode: the AI providers, the optional integrations, and the operator's controls over each. Read when you assess where a deployment's data goes or answer a subprocessor question."
---

# Data Flows & Subprocessors

**On this page:** What stays inside the deployment · What leaves the deployment, and to whom · Provider retention & training posture · Operator checklist · Related pages

This page describes **what document and workflow content leaves the customer's deployment**, **which external services receive it**, and **what controls the operator has** over that flow, as the shipped code behaves today. It is intended for a customer's security or procurement reviewer doing a data-flow assessment.

> [!NOTE]
> **Deployment model first**
>
> The platform is **not a hosted SaaS**. It is deployed on-premises or inside the customer's own cloud, and every external call is made with **customer-owned credentials** (the customer's AWS Bedrock IAM role, their Entra ID tenant, or their direct Anthropic/Cohere keys). The platform's maintainers do not sit in the data path of a customer deployment and do not receive customer document content.

## What stays inside the deployment

The following never leave the customer environment under normal operation — they are persisted only in the customer-controlled databases and object store:

- Uploaded source documents and their extracted text/pages.
- Workflow templates, executed-workflow records, block responses, and generated outputs.
- Vector embeddings (computed remotely, but **stored** in the customer's `UserData` database).
- User accounts, roles, and the audit histories ([Audit records](../architecture/multi-tenant/index.md#audit-records)).

Storage is the customer's PostgreSQL (or SQL Server) plus the configured file store (database by default, or the customer's own S3 / MinIO). See [Application Configuration → File storage](../operations/application-configuration.md#file-storage).

## What leaves the deployment, and to whom

Two categories of content cross the boundary: **AI-provider calls** (always present — the platform cannot generate or embed without them) and **optional outbound integrations** (present only if the operator configures them).

### AI providers (chat + embeddings)

These are the load-bearing subprocessors. The **active** provider is chosen once at startup by deployment mode — a deployment uses exactly one chat backend and one embeddings backend, never several at once. `CloudHost` picks the mode: Azure when `FORCE_AZURE_MODE` is `true`; AWS when `FORCE_AWS_MODE` is `true`, or when neither variable is set and the process runs on an EC2 host; direct otherwise. Setting both to `true` stops startup. See [AI providers & connectors](../architecture/ai-integration/providers.md) for how the backend is selected.

| Subprocessor | Reached in mode | Content sent | Data category |
|--------------|-----------------|--------------|---------------|
| **AWS Bedrock** (Anthropic Claude + Cohere Embed) | AWS (`FORCE_AWS_MODE=true`, or an EC2 host with no mode set) | Prompts incl. document text/excerpts; document chunks for embedding | Customer document content + workflow prompts |
| **Anthropic public API** | direct (neither AWS nor Azure) | Prompts incl. document text/excerpts | Customer document content + workflow prompts |
| **Cohere public API** | direct (neither AWS nor Azure) | Document chunks to embed | Customer document content |
| **Azure AI Foundry** (Claude + Cohere) | Azure (`FORCE_AZURE_MODE=true`) | Prompts + document chunks for embedding | Customer document content + workflow prompts |


The agent runtime, which runs agent blocks when a deployment enables it, is a service inside the deployment. In its default `bedrock` mode it calls Claude on Bedrock (`CLAUDE_CODE_USE_BEDROCK=1`), under the same Bedrock terms.


In every mode the **request payload includes the relevant document content** — chat prompts carry the document text (or RAG-selected excerpts) the workflow reasons over, and embedding calls carry the document chunks being indexed. This is inherent to the product: there is no mode in which documents are processed entirely on-device.

> [!TIP]
> **One provider family**
>
> Chat is **always Anthropic Claude**; embeddings are **always Cohere**. There is no OpenAI/GPT path anywhere in the codebase. The variation is purely *which backend* hosts Claude/Cohere and *whose credentials* reach it. See [AI Integration](../architecture/ai-integration/index.md).

### Optional outbound integrations

These fire only when a workflow contains the block or the feature is in use. Treat each as a data-egress point. Keys are listed in [Application Configuration → AI provider keys](../operations/application-configuration.md#ai-provider-keys).

| Integration | Triggered by | Content sent |
|-------------|--------------|--------------|
| Exa (`api.exa.ai`) | Intelligent Search block | Search query strings, domain filters |
| ExchangeRate-API (`v6.exchangerate-api.com`) | Exchange Rate block | Currency code and date; the API key travels in the request path |
| Any public host a workflow names | Web API Call block | The request the block's templates render, which can carry earlier block outputs drawn from documents. Internal addresses are refused unless `AllowedInternalHosts` lists them ([Calling an internal service](../operations/internal-service-calls.md)) |
| The mail provider (SMTP or SendGrid) | Emailed sign-in codes and password-reset mails | The recipient address and the code or reset link |
| Google or the OIDC identity provider | Google or OIDC sign-in | The sign-in exchange; the platform receives the user's identity claims |


There is no Google search, SerpAPI or external PDF generation service, and no setting for one.

## Provider retention & training posture

The platform does **not** control these third parties' retention terms — the operator does, through their own contract with each provider. The summary below reflects each provider's **commercial/enterprise** posture and is a starting point for a security review, **not** a substitute for the provider's current terms (these policies change; verify against the linked source before relying on a specific figure).

### AWS Bedrock

- Model providers have no access to the accounts that run Bedrock models, so they do not see Bedrock logs or customer prompts and completions ([AWS — Data protection in Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/data-protection.html)).
- **Retention is a per-Region setting of the customer's account.** Under `none` nothing is written to durable storage. Under `default` each model's own policy applies, and AWS may retain data for abuse detection; the model provider does not receive it. Claude Fable 5 and Claude Fable 5.1 require `aws_review`: prompts and completions are kept within AWS for up to 30 days and may be reviewed by AWS, and are not shared with the model provider. Under `none` or `default` those two models are unavailable ([AWS — Bedrock data retention](https://docs.aws.amazon.com/bedrock/latest/userguide/data-retention.html)).
- **Operator control — invocation logging:** the operator may turn on *model invocation logging*, which captures prompts, responses, and metadata to an S3 bucket or CloudWatch log group in the same account and Region. It is off by default ([AWS — Model invocation logging](https://docs.aws.amazon.com/bedrock/latest/userguide/model-invocation-logging.html)).
- Calls are SigV4-signed with the customer's IAM principal; the principal needs `bedrock:InvokeModel` on the configured model IDs in the deployment region. See [Minimal Bedrock configuration](../operations/application-configuration.md#minimal-bedrock-configuration).

### Anthropic public API

- Retained data is never used for model training without the customer's express permission ([Anthropic — API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)).
- Anthropic deletes API inputs and outputs within 30 days of receipt or generation, except where the customer controls a longer retention, where the parties agreed otherwise, or where Anthropic must keep them to enforce its Usage Policy or comply with law ([Anthropic — commercial data retention](https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data)). Zero data retention (ZDR) is a separate arrangement with Anthropic.
- **Operator control:** the platform sends the customer's `X-Api-Key`, so the contractual posture, including any ZDR arrangement, is the customer's.

### Azure AI Foundry

- Calls go to the Foundry resource in the customer's Azure subscription. For Claude in Microsoft Foundry, Anthropic is the data processor, so Anthropic's terms above apply to Claude calls ([Anthropic — API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)). Cohere models on Foundry follow their Marketplace terms. See [Azure AI Foundry Integration](../architecture/ai-integration/azure-foundry.md).

### Cohere

- Embeddings are sent to Cohere through whichever backend the deployment uses (Bedrock, Azure Foundry, or the direct Cohere API). When reached via **Bedrock or Azure**, the embeddings call inherits that platform's retention/training posture above; when reached via the **direct Cohere API**, the customer's Cohere enterprise terms govern. Only document chunks (not full workflow context) are sent for embedding.

## Operator checklist

- Pick exactly one provider mode and supply **only** that mode's credentials.
- Leave the Exa and ExchangeRate-API keys **empty** unless a workflow needs them, and review the hosts your Web API Call blocks name.
- On Bedrock, set the account's data retention mode on purpose: `none` keeps nothing but makes Claude Fable 5 and 5.1 unavailable; `aws_review` lets AWS keep and review prompts for up to 30 days.
- For an auditable egress record on AWS, enable **Bedrock model invocation logging** to your own S3/CloudWatch.
- On the Anthropic API, arrange zero data retention through your own Anthropic agreement if your data needs it.
- Keep file storage and both databases inside your trust boundary; embeddings are stored locally even though they are computed remotely.

## Related pages

- [AI providers & connectors](../architecture/ai-integration/providers.md) — how the active backend and credentials are selected
- [AI Integration overview](../architecture/ai-integration/index.md)
- [Application Configuration Reference](../operations/application-configuration.md) — every provider key, with placeholders
- [Azure AI Foundry Integration](../architecture/ai-integration/azure-foundry.md) — the Azure-mode data path

