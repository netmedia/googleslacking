# Google Cloud and Workspace setup

All of this is done once per GCP project. In hosted mode that's Netmedia's project. In self-hosted mode it's the customer's own project.

## 1. GCP project and APIs

```bash
gcloud projects create googleslacking-prod --organization=<ORG_ID>
gcloud config set project googleslacking-prod
gcloud services enable chat.googleapis.com workspaceevents.googleapis.com pubsub.googleapis.com people.googleapis.com
```

## 2. OAuth consent screen (Google Auth Platform)

- **User type:**
  - *Internal*: Netmedia only, or self-hosted at a customer. No verification needed.
  - *External*: when other organisations should use Netmedia's hosted instance. Their admin marks the app as **Trusted**, so it counts as internal for them: no 100-user cap and refresh tokens don't expire after 7 days. Don't start verification unless you want to offer it publicly; `chat.messages` is a *restricted* scope (requires a CASA assessment).
- Scopes: `openid`, `email`, `profile`, `chat.spaces.readonly`, `chat.spaces.create`, `chat.messages`, `chat.messages.reactions`, `chat.memberships`, `chat.users.readstate`, `directory.readonly`.
- When the scopes change, users must sign in again (the app always asks for consent at sign-in, so it's enough to sign out and back in).

## 3. OAuth client (Web application)

Authorized redirect URIs:
- `http://localhost:5173/auth/callback` (dev, via the Vite proxy)
- `https://<your-domain>/auth/callback` (prod)

Put the client ID and secret in user secrets (see README) or Key Vault.

## 4. Chat app configuration

Console → Google Chat API → **Configuration**: name "GoogleSlacking", avatar URL and description. Interactive features can be off, because we only use the API with user auth. Under *Visibility*, choose the people or groups in the domain who can use the app.

## 5. Pub/Sub

```bash
gcloud pubsub topics create chat-events

# The Workspace Events API publishes as this service account:
gcloud pubsub topics add-iam-policy-binding chat-events \
  --member="serviceAccount:chat-api-push@system.gserviceaccount.com" \
  --role="roles/pubsub.publisher"

gcloud pubsub subscriptions create chat-events-backend \
  --topic=chat-events --ack-deadline=60 --message-retention-duration=1d

# Backend service account (pull only)
gcloud iam service-accounts create googleslacking-backend
gcloud pubsub subscriptions add-iam-policy-binding chat-events-backend \
  --member="serviceAccount:googleslacking-backend@googleslacking-prod.iam.gserviceaccount.com" \
  --role="roles/pubsub.subscriber"
gcloud iam service-accounts keys create C:\secrets\googleslacking-pubsub.json \
  --iam-account=googleslacking-backend@googleslacking-prod.iam.gserviceaccount.com
```

Keep the key outside the repo (`.gitignore` blocks `service-account*.json` and `secrets/`). Long term, switch to Workload Identity Federation (Azure Managed Identity → GCP) so there's no key at all.

Recommended: a dead-letter topic on `chat-events-backend` (max 5 deliveries).

## 6. Admin of each customer Workspace (hosted mode)

Admin console → Security → Access and data control → **API controls** → Manage third-party app access → *Add app* → OAuth App Name or Client ID → enter the client ID → **Trusted**.

Then add the tenant to the `Tenants` table (Name, Domain = the primary Workspace domain).

## 7. Web Push (VAPID)

Push notifications in the browser and the installed PWA don't go through Google; we send them ourselves (RFC 8030/8291) using a VAPID key pair:

```powershell
cd src/GoogleSlacking.Api
dotnet run -- --generate-vapid   # prints the 3 user-secrets commands (Push:VapidPublicKey, Push:VapidPrivateKey, Push:Subject)
```

- One key pair per deployment. If you change it, all existing subscriptions stop working (users re-enable push in Settings).
- In production: Key Vault / app settings `Push__VapidPublicKey`, `Push__VapidPrivateKey`, `Push__Subject`.
- iPhone/iPad: push works only for the PWA added to the home screen (iOS 16.4+).
