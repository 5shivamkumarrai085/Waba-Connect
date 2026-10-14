# Load test: inbound WhatsApp messages and bot replies

This kit checks that the app keeps up when many customers write at once. Nothing has been run
against shared infrastructure: the development database is shared with other developers and a
hosted server, so a load test there would disturb them. Run it on an environment of its own.

## What it exercises

`webhook-load.js` (k6) posts signed Meta webhooks for thousands of fictional customers
(`+44 7700 9xxxxx`). Each message goes through the real pipeline:

- signature check;
- the queue, ordered per customer;
- storing the message;
- the bot engine;
- the reply.

The reply goes to `graph_stub.py`, not to Meta, so no real phone is ever messaged.

## Set-up (staging or a throwaway environment, never the shared dev database)

1. **Database.** Use a database of its own (for example a Neon branch made for the test and
   deleted afterwards) and apply the migrations.
2. **Graph stub.** Start it: `python tools/loadtest/graph_stub.py 8099`.
3. **Backend settings.** Start the backend under test with:
   - `WhatsApp__GraphBaseUrl=http://<stub-host>:8099`, so replies go to the stub;
   - `WhatsApp__AppSecret=<any test secret>`, used to sign the requests;
   - `WhatsApp__Webhook__Concurrency=8`, the inbound loops per instance (default 4).
4. **WhatsApp test connection.** Create one and give it a phone number id (any string); that is
   `PHONE_NUMBER_ID` below. Add the bot flows you want to exercise (e.g. a "hi" menu and
   "send money").
5. **Run k6:**

   ```bash
   k6 run -e BASE_URL=https://<backend> -e APP_SECRET=<same secret> -e PHONE_NUMBER_ID=<id> -e RATE=200 -e CUSTOMERS=5000 tools/loadtest/webhook-load.js
   ```

   `RATE` is messages per second at the plateau. Increase it step by step: 50, 200, 500, 1000.

## What to watch

| Signal | Where | Healthy |
|---|---|---|
| Webhook latency | k6 `http_req_duration` | p95 < 300 ms (the endpoint only verifies and stores) |
| Webhook errors | k6 `http_req_failed` | < 0.1 % |
| Inbound backlog | `GET /api/email/queue/stats` (all queues, `wa-webhook.v4` included; needs the EmailQueue.View permission), or `SELECT count(*) FROM "JobQueue" WHERE "QueueName" = 'wa-webhook.v4' AND "Status" = 'Pending'` | Flat during the plateau. A steadily growing backlog means replies are slower than arrivals: add instances or loops |
| Reply throughput | `GET http://<stub>:8099/` (sends received) | Close to the arrival rate × replies per message |
| Dead letters | `"JobQueue"` rows with `Status = 'DeadLettered'` | 0 |
| Database | Neon dashboard: connections, CPU, slow queries | Connections below the pool limit; no query regularly > 100 ms |

## Scaling notes

- **Throughput per instance** is about `Concurrency ÷ time per bot turn`. A bot turn is a few
  database round trips plus one or two Graph calls, so 0.3–1 s. With 4 loops, expect roughly
  4–12 customer messages per second per instance. Add instances (all of them claim from the same
  queue safely) or loops.
- **Ordering:** one customer's messages are handled one at a time, in order, across all instances
  (the ordered claim). Different customers run in parallel.
- **Meta's own limit** is about 80 messages per second per business number by default (Meta raises
  it for high-quality numbers). Beyond that, add numbers, or ask Meta for a higher tier.
- **More than one instance** needs the SignalR Redis backplane (`SignalR:Redis`) so live inbox
  updates reach every agent. Sweeps (SLA auto-close, template sync, Send Money maintenance) already
  run on one instance at a time through a database lock.
