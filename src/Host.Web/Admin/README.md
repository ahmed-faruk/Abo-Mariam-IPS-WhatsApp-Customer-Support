# Admin Lite (Issue #14)

Admin Lite is the smallest local-only operator surface the Controlled Client Demo needs
(docs/TECHNICAL.md section 36.2). It is **not** the Full MVP Admin.

## Screens

| Path | What it does |
|---|---|
| `/admin/catalog` | Every variant with model, grade, price and quantity. Price and quantity edits go through `ICatalogCommercialUpdates` under the audit actor `demo-operator`. |
| `/admin/business-info` | The approved business-info answers. Only `WorkingHours` is editable, through `IStorefrontBusinessInfoUpdates`. |
| `/admin/conversations` | Conversations with customer, current mode and last activity. |
| `/admin/conversations/{id}` | The current mode and the chronological transcript. |

All pages use module-owned contracts only; Admin code never touches a module `DbContext` or table.
Every post is protected by ASP.NET Core antiforgery validation.

## Exposure: a demo-only decision, not a security pattern

The host listens on two loopback endpoints: the public listener `127.0.0.1:5000` (the only one the
Cloudflare Quick Tunnel may expose) and the admin listener `127.0.0.1:5001`. `AdminLitePortGuard`
answers every `/admin` request as **404 Not Found** unless `HttpContext.Connection.LocalPort` equals
`AdminLite:Port`; with no port configured, `/admin` is not found anywhere. There is no login.

This is acceptable only because the admin listener is bound to loopback on the operator's machine for
the controlled demo. It must not be copied: the pilot and production requirement stays the
authenticated Admin of docs/TECHNICAL.md sections 18 and 19.

## Transcript limitation

Messaging does not link inbound rows to a conversation, and the fast track does not redesign it. The
transcript therefore shows:

- outbound rows whose `conversation_id` is the selected conversation;
- inbound rows whose `customer_external_id` is the selected conversation's customer, with **no**
  lower bound from the conversation start (the first inbound message is stored before its
  conversation is created, so its timestamp can precede `started_at`);
- one order: time (inbound provider timestamp, outbound creation time), inbound before outbound at
  the same instant, then row id.

This is correct only for the **reset-controlled single-conversation demo state**: the documented
reset clears the demo customer's Messaging and Conversations rows, so the customer has exactly one
fresh conversation. If a customer has several conversations without a reset, Admin Lite does not
attribute inbound messages to the right one. Reliable historical attribution is deferred until after
client validation.
