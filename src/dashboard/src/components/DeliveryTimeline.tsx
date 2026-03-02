import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis
} from "recharts";
import type { TimelineBucket } from "../types";
import { formatLocaleDateTime, formatLocaleTime } from "../utils/dateTime";

function formatLabel(value: string): string {
  return formatLocaleTime(value);
}

export function DeliveryTimeline({ buckets }: { buckets: TimelineBucket[] }) {
  return (
    <div className="rounded-xl border border-border bg-surface-1 p-4 animate-fade-in-up">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold text-text-primary">Delivery Timeline</h2>
        <span className="text-xs text-text-muted">Last 24 hours</span>
      </div>

      <div className="h-64">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={buckets} margin={{ top: 4, right: 0, left: -20, bottom: 0 }}>
            <defs>
              <linearGradient id="deliveredGrad" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="#34d399" stopOpacity={0.3} />
                <stop offset="100%" stopColor="#34d399" stopOpacity={0} />
              </linearGradient>
              <linearGradient id="failedGrad" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="#f87171" stopOpacity={0.25} />
                <stop offset="100%" stopColor="#f87171" stopOpacity={0} />
              </linearGradient>
            </defs>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(63,63,70,0.5)" vertical={false} />
            <XAxis
              dataKey="timestamp"
              tickFormatter={formatLabel}
              tick={{ fill: "#71717a", fontSize: 11 }}
              axisLine={false}
              tickLine={false}
            />
            <YAxis
              tick={{ fill: "#71717a", fontSize: 11 }}
              axisLine={false}
              tickLine={false}
            />
            <Tooltip
              labelFormatter={(value) => formatLocaleDateTime(value as string)}
              contentStyle={{
                borderRadius: 8,
                border: "1px solid #27272a",
                background: "#18181b",
                color: "#fafafa",
                fontSize: 12,
                boxShadow: "0 8px 32px rgba(0,0,0,0.5)"
              }}
              itemStyle={{ color: "#a1a1aa", fontSize: 12 }}
            />
            <Area
              type="monotone"
              dataKey="delivered"
              stroke="#34d399"
              strokeWidth={1.5}
              fill="url(#deliveredGrad)"
              name="Delivered"
            />
            <Area
              type="monotone"
              dataKey="failed"
              stroke="#f87171"
              strokeWidth={1.5}
              fill="url(#failedGrad)"
              name="Failed"
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
