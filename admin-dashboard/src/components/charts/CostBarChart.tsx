'use client';

import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';

interface CostBarChartProps {
  data: { [key: string]: number | string }[];
  dataKey: string;
  nameKey: string;
  color?: string;
  label?: string;
  formatter?: (value: number) => string;
}

export function CostBarChart({ data, dataKey, nameKey, color = '#8b5cf6', label, formatter }: CostBarChartProps) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-6">
      {label && <h3 className="text-gray-700 font-semibold mb-4">{label}</h3>}
      <ResponsiveContainer width="100%" height={240}>
        <BarChart data={data} margin={{ top: 5, right: 5, left: 0, bottom: 5 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" vertical={false} />
          <XAxis dataKey={nameKey} tick={{ fontSize: 11, fill: '#9ca3af' }} tickLine={false} axisLine={false} />
          <YAxis tick={{ fontSize: 11, fill: '#9ca3af' }} tickLine={false} axisLine={false} tickFormatter={formatter} />
          <Tooltip
            contentStyle={{ background: '#1f2937', border: 'none', borderRadius: 8, color: '#fff', fontSize: 12 }}
            formatter={formatter ? (val: number) => [formatter(val), dataKey] : undefined}
          />
          <Bar dataKey={dataKey} fill={color} radius={[4, 4, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
