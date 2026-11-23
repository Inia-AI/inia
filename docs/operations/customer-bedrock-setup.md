---
type: runbook
status: current
description: "Production checklist for pointing a deployment at the customer's own AWS Bedrock: AWS mode, the IAM policy for Claude and the Cohere embeddings, the instance profile, the region, console model access, the agent runtime's own role, and the post-deploy AI smoke test. Read when a self-hosted deployment will call Bedrock in the customer's AWS account, or a Bedrock call fails with AccessDenied."
---

# Customer-Owned Bedrock Setup (production)

**On this page:** Four things must line up · Post-deploy AI smoke test · Done when · Related pages

A self-hosted IniaAi deployment that uses AWS Bedrock for AI calls points at the **customer's own** AWS account — the platform never ships Inia AWS credentials. In native AWS mode the chat provider (`AwsBedrockChatProvider`) and the embeddings provider (`CohereAwsEmbeddingsConnector`) hold **no API key**: each call to `AmazonBedrockRuntimeClient.InvokeModelAsync` is signed with the host's ambient AWS credentials via SigV4 (`platform/IniaAi.AiConnection/AwsBedrock/AwsBedrockChatProvider.cs`, `platform/IniaAi.AiConnection/Cohere/CohereAwsEmbeddingsConnector.cs`).

This is the **production deployer's** checklist. For the connector internals see [AI providers & connectors](../architecture/ai-integration/providers.md#awsbedrockchatprovider-native-aws-bedrock).


> [!NOTE]
> **Per-deployment values**
>
> The account id, region, and the exact model tiers a deployment uses are **per-deployment** and supplied by the deployment owner. Do not invent them.
>

## Four things must line up

### 1. Select AWS mode

Set `FORCE_AWS_MODE=true`. This makes `PlatformSettings.IsRunningInAws` true and wires the native Bedrock chat + Cohere-on-Bedrock embeddings connectors (`AddModelConnectors` in `InfrastructureServiceRegistration.cs`). On a customer EC2 host the mode is **also auto-detected from instance metadata**, so it engages without the flag. The minimal `appsettings.json` only needs the Bedrock-mode model tiers — see [Application Configuration → Minimal Bedrock configuration](application-configuration.md#minimal-bedrock-configuration).

### 2. Supply credentials with Bedrock permissions

Both Bedrock clients are created with **no explicit credentials**, so the AWS SDK resolves them from the ambient chain — the **EC2 instance role** in a deployed environment, or the standard `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` / `AWS_SESSION_TOKEN` environment variables / shared profile elsewhere. The IAM principal needs `bedrock:InvokeModel` on the **exact model / inference-profile IDs** the deployment uses, in the deployment region — and that includes the **Cohere embeddings** model, because RAG indexing and retrieval both call it.

A minimal policy covering the default tiers (`AdvancedModel = UsAwsSonnet5`, `BasicModel = UsAwsHaiku5_5`, `Cohere.EmbeddingsModel = AwsEmbedV40`):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ClaudeChat",
      "Effect": "Allow",
      "Action": ["bedrock:InvokeModel"],
      "Resource": [
        "arn:aws:bedrock:<region>:<account-id>:inference-profile/us.anthropic.claude-sonnet-5",
        "arn:aws:bedrock:<region>:<account-id>:inference-profile/us.anthropic.claude-haiku-5-5",
        "arn:aws:bedrock:*::foundation-model/anthropic.claude-*"
      ]
    },
    {
      "Sid": "CohereEmbeddings",
      "Effect": "Allow",
      "Action": ["bedrock:InvokeModel"],
      "Resource": [
        "arn:aws:bedrock:<region>:<account-id>:inference-profile/global.cohere.embed-v4:0",
        "arn:aws:bedrock:*::foundation-model/cohere.embed-v4*"
      ]
    }
  ]
}
```

A cross-region inference profile (`us.` / `eu.` / `global.`) fans out to the underlying foundation models, which is why each statement also allows the matching `foundation-model/*` ARNs. The default embeddings model is `global.cohere.embed-v4:0` (`AiModel.AwsEmbedV40`); a deployment pinned to the US profile uses `us.cohere.embed-v4:0` (`UsAwsEmbedV40`), and one pinned to the EU profile `eu.cohere.embed-v4:0` (`EuAwsEmbedV40`) — match the ARN to the [enum value the deployment configures](../architecture/ai-integration/providers.md#valid-aimodel-enum-values).

#### Apply the policy and attach it to the host

The platform host picks up credentials from the **ambient AWS chain** — neither Bedrock client passes explicit credentials (`new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig { … })` in both `AwsBedrockChatProvider.cs` and `CohereAwsEmbeddingsConnector.cs`). On EC2 that means an **instance profile** (an IAM role attached to the instance); the SDK resolves it with no extra config. So the production way to grant Bedrock access is: turn the JSON above into an IAM policy, attach it to a role the EC2 host can assume, and attach that role's instance profile to the host.

Save the policy JSON from above as `bedrock-policy.json` (with `<region>` / `<account-id>` filled in — these are per-deployment values the deployment owner gives you), then, with AWS CLI credentials for the **customer's** account:

```bash
# 1. Create the IAM policy from the JSON
aws iam create-policy \
  --policy-name IniaAiBedrockInvoke \
  --policy-document file://bedrock-policy.json

# 2. Create a role the EC2 host can assume (EC2 trust policy)
cat > ec2-trust.json <<'EOF'
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": { "Service": "ec2.amazonaws.com" },
      "Action": "sts:AssumeRole"
    }
  ]
}
EOF
aws iam create-role \
  --role-name IniaAiBedrockHostRole \
  --assume-role-policy-document file://ec2-trust.json

# 3. Attach the Bedrock policy to the role
aws iam attach-role-policy \
  --role-name IniaAiBedrockHostRole \
  --policy-arn arn:aws:iam::<account-id>:policy/IniaAiBedrockInvoke

# 4. Create an instance profile and put the role in it
aws iam create-instance-profile --instance-profile-name IniaAiBedrockHostProfile
aws iam add-role-to-instance-profile \
  --instance-profile-name IniaAiBedrockHostProfile \
  --role-name IniaAiBedrockHostRole

# 5. Attach the instance profile to the platform host
aws ec2 associate-iam-instance-profile \
  --instance-id <platform-instance-id> \
  --iam-instance-profile Name=IniaAiBedrockHostProfile
```

Replace `<account-id>` and `<platform-instance-id>` with the deployment's own values. Background on the role-vs-instance-profile relationship is in the [AWS IAM roles for EC2](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/iam-roles-for-amazon-ec2.html) and [instance profile](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_use_switch-role-ec2_instance-profiles.html) documentation.


#### The agent runtime has its own role

When the deployment runs the agent runtime beside the platform, the runtime's Bedrock calls do not use the host role. Because the runtime executes model-written code, it gets a role that can do nothing but `bedrock:InvokeModel` and `bedrock:InvokeModelWithResponseStream`. The host role may assume it, and the runtime assumes it and hands only those short-lived credentials to the code it runs. A host you provision yourself needs the same split: a second role with the streaming permission on the models in `PlatformSettings:AgentRuntime:AllowedModels`, which the host role may assume.


If instead the host runs **outside EC2** (or you deliberately use static keys), supply the same permissions through `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` (and `AWS_SESSION_TOKEN` for temporary credentials) on the container, attached to an IAM user/role carrying the `IniaAiBedrockInvoke` policy. The instance-profile path is preferred for EC2 because there is no long-lived secret to rotate.

### 3. Set the region

Both clients are created **without** an explicit `RegionEndpoint`, so the SDK reads the region from the ambient environment — `AWS_REGION` / `AWS_DEFAULT_REGION`, the instance metadata, or the active profile. The region **must match the model-ID prefix**: a `us.` profile only resolves in `us-*` regions, an `eu.` profile in `eu-*` regions; `global.` profiles span regions. A mismatch surfaces as `model-not-found` / validation errors.

### 4. Enable model access in the customer's Bedrock console

Bedrock model access is **off by default, per account and per region**. **Who does this:** the **customer's own AWS account administrator** — the person with Bedrock console access in the deployment account and region. It is not something Inia AI can enable remotely on a self-hosted deployment, and it is not granted by the IAM policy above (the IAM policy authorizes the call; console model-access grants the account the right to use the model at all). In the customer's AWS console, open the Bedrock model-access page for the deployment region and request access for each Claude model **and** the Cohere `embed-v4` model the deployment uses; wait until each reads **Access granted**. Without this, correctly-signed requests still fail with `AccessDenied`.

> [!WARNING]
> **`AccessDenied` after the IAM policy is correct**
>
> If the smoke test below returns `AccessDenied` even though the IAM policy is right, the cause is almost always step 4 — model access not yet granted in the console for that account/region — or a region/profile-prefix mismatch (step 3).

## Post-deploy AI smoke test

Run this **after a deploy** to confirm the platform can actually reach the model end to end. It does not depend on a configured workflow — `POST /api/v1/ai/content-refinement` makes a single real Claude call through whichever connector is active and returns the refined text. Get an access token with the [password grant](../reference/api/authentication.md#1-password-grant-user-authentication), then send the call with that token and the `X-Tenant-Id` header, and a JSON body whose `content` holds a short text with typing errors and whose `instruction` asks to fix them.


A `200 OK` whose JSON `refinedContent` differs from what you sent proves credentials, region, IAM `bedrock:InvokeModel`, and console model access are all working for the **chat** path. To exercise the **embeddings** (Bedrock Cohere) path as well, run any workflow that indexes a document — RAG indexing fails fast if `bedrock:InvokeModel` on the `embed-v4` profile is missing.

## Done when

- The smoke test's `POST /api/v1/ai/content-refinement` answers `200 OK` with a `refinedContent` that differs from what you sent.
- A workflow run that indexes a document finishes without a Bedrock error, which proves the Cohere embeddings path.

## Related pages

- [Release Package Deployment](release-package-deployment.md) — the surrounding deploy flow this Bedrock setup slots into.
- [Application Configuration → Minimal Bedrock configuration](application-configuration.md#minimal-bedrock-configuration) — the `appsettings.json` keys.
- [AI providers & connectors](../architecture/ai-integration/providers.md) — connector internals, the full `AiModel` enum → model-ID table, and the Cohere embeddings models.

