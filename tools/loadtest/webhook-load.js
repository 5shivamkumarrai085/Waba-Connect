// k6 load test for the inbound WhatsApp path: signed Meta webhooks for many customers at once.
//
//   k6 run -e BASE_URL=https://staging.example.com -e APP_SECRET=... -e PHONE_NUMBER_ID=... tools/loadtest/webhook-load.js
//
// Run it ONLY against a separate environment (its own database) whose WhatsApp:GraphBaseUrl points
// at tools/loadtest/graph_stub.py — never against the shared development database or a real number.
// See tools/loadtest/README.md.
import http from 'k6/http';
import crypto from 'k6/crypto';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL;
const APP_SECRET = __ENV.APP_SECRET;
const PHONE_NUMBER_ID = __ENV.PHONE_NUMBER_ID;
// Fictional customers: Ofcom's drama range +44 7700 900000–900999, then a synthetic block.
const CUSTOMERS = parseInt(__ENV.CUSTOMERS || '5000', 10);
const KEYWORDS = (__ENV.KEYWORDS || 'hi,send money,Option A,thanks').split(',');

if (!BASE_URL || !APP_SECRET || !PHONE_NUMBER_ID) {
  throw new Error('Set BASE_URL, APP_SECRET and PHONE_NUMBER_ID (see README.md).');
}

export const options = {
  scenarios: {
    // Ramp to the target arrival rate, hold it, then stop: messages per second, not users.
    inbound: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 2000,
      stages: [
        { target: parseInt(__ENV.RATE || '200', 10), duration: '2m' },
        { target: parseInt(__ENV.RATE || '200', 10), duration: '10m' },
        { target: 0, duration: '1m' },
      ],
    },
  },
  thresholds: {
    // Meta needs a 200 quickly; the endpoint only verifies, stores and acknowledges.
    http_req_duration: ['p(95)<300', 'p(99)<1000'],
    http_req_failed: ['rate<0.001'],
  },
};

const accepted = new Counter('webhooks_accepted');

function customer() {
  const n = Math.floor(Math.random() * CUSTOMERS);
  return `4477009${String(n).padStart(5, '0')}`;
}

export default function () {
  const from = customer();
  const now = Math.floor(Date.now() / 1000);
  const body = JSON.stringify({
    object: 'whatsapp_business_account',
    entry: [{
      id: 'loadtest',
      changes: [{
        field: 'messages',
        value: {
          messaging_product: 'whatsapp',
          metadata: { display_phone_number: '000', phone_number_id: PHONE_NUMBER_ID },
          contacts: [{ profile: { name: `Load ${from.slice(-4)}` }, wa_id: from }],
          messages: [{
            from,
            id: `wamid.load.${from}.${now}.${Math.random().toString(36).slice(2)}`,
            timestamp: String(now),
            type: 'text',
            text: { body: KEYWORDS[Math.floor(Math.random() * KEYWORDS.length)] },
          }],
        },
      }],
    }],
  });

  const signature = 'sha256=' + crypto.hmac('sha256', APP_SECRET, body, 'hex');
  const res = http.post(`${BASE_URL}/api/webhook/whatsapp`, body, {
    headers: { 'Content-Type': 'application/json', 'X-Hub-Signature-256': signature },
  });

  if (check(res, { 'acknowledged with 200': (r) => r.status === 200 })) accepted.add(1);
}
