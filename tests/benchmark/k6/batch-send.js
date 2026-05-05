import http from "k6/http";
import { check } from "k6";

const API_BASE = __ENV.API_BASE || "http://host.docker.internal:5100";
const API_KEY = __ENV.API_KEY;
const BATCH_SIZE = Number(__ENV.BATCH_SIZE || 50);

if (!API_KEY) {
  throw new Error("API_KEY env var is required (run seed.sh first).");
}

export const options = {
  scenarios: {
    sustained: {
      executor: "constant-arrival-rate",
      rate: Number(__ENV.RATE || 50),
      timeUnit: "1s",
      duration: __ENV.DURATION || "60s",
      preAllocatedVUs: 30,
      maxVUs: 100,
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<2000"],
  },
};

const items = [];
for (let i = 0; i < BATCH_SIZE; i++) {
  items.push({ eventType: "bench.event", payload: { idx: i } });
}
const body = JSON.stringify({ messages: items });

export default function () {
  const res = http.post(`${API_BASE}/api/v1/messages/batch`, body, {
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${API_KEY}`,
    },
  });

  check(res, {
    "status is 202": (r) => r.status === 202,
    "all accepted": (r) => {
      try {
        const data = r.json("data");
        return data.acceptedEvents === BATCH_SIZE;
      } catch {
        return false;
      }
    },
  });
}
