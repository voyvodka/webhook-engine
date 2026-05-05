import http from "k6/http";
import { check } from "k6";

const API_BASE = __ENV.API_BASE || "http://host.docker.internal:5100";
const API_KEY = __ENV.API_KEY;

if (!API_KEY) {
  throw new Error("API_KEY env var is required (run seed.sh first).");
}

const headers = {
  "Content-Type": "application/json",
  Authorization: `Bearer ${API_KEY}`,
};

export const options = {
  scenarios: {
    sends: {
      executor: "constant-arrival-rate",
      exec: "send",
      rate: Number(__ENV.SEND_RATE || 300),
      timeUnit: "1s",
      duration: __ENV.DURATION || "60s",
      preAllocatedVUs: 30,
      maxVUs: 150,
    },
    lists: {
      executor: "constant-arrival-rate",
      exec: "list",
      rate: Number(__ENV.LIST_RATE || 50),
      timeUnit: "1s",
      duration: __ENV.DURATION || "60s",
      preAllocatedVUs: 10,
      maxVUs: 30,
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.02"],
  },
};

export function send() {
  const res = http.post(
    `${API_BASE}/api/v1/messages`,
    JSON.stringify({
      eventType: "bench.event",
      payload: { orderId: `ord_${__VU}_${__ITER}` },
    }),
    { headers }
  );
  check(res, { "send 202": (r) => r.status === 202 });
}

export function list() {
  const res = http.get(`${API_BASE}/api/v1/messages?page=1&pageSize=20`, { headers });
  check(res, { "list 200": (r) => r.status === 200 });
}
