import http from "k6/http";
import { check } from "k6";

const API_BASE = __ENV.API_BASE || "http://host.docker.internal:5100";
const API_KEY = __ENV.API_KEY;

if (!API_KEY) {
  throw new Error("API_KEY env var is required (run seed.sh first).");
}

export const options = {
  scenarios: {
    sustained: {
      executor: "constant-arrival-rate",
      rate: Number(__ENV.RATE || 500),
      timeUnit: "1s",
      duration: __ENV.DURATION || "60s",
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<500", "p(99)<1500"],
  },
};

export default function () {
  const payload = JSON.stringify({
    eventType: "bench.event",
    payload: {
      orderId: `ord_${__VU}_${__ITER}`,
      amount: 100 + (__ITER % 1000),
    },
  });

  const res = http.post(`${API_BASE}/api/v1/messages`, payload, {
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${API_KEY}`,
    },
  });

  check(res, {
    "status is 202": (r) => r.status === 202,
    "has messageId": (r) => r.status === 202 && r.body.length > 0,
  });
}
