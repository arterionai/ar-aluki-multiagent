'use client';

import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer, Legend } from 'recharts';

const COLORS = ['#3b82f6', '#8b5cf6', '#10b981', '#f59e0b', '#ef4444', '#06b6d4', '#ec4899'];

interface PieBreakdownProps {
  data: { name: string; value: number }[];
  label?: string;
}

export function PieBreakdown({ data, label }: PieBreakdownProps) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-6">
      {label && <h3 className="text-gray-700 font-semibold mb-4">{label}</h3>}
      <ResponsiveContainer width="100%" height={240}>
        <PieChart>
          <Pie
            data={data}
            cx="50%"
            cy="45%"
            innerRadius={60}
            outerRadius={90}
            paddingAngle={3}
            dataKey="value"
          >
            {data.map((_, index) => (
              <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{ background: '#1f2937', border: 'none', borderRadius: 8, color: '#fff', fontSize: 12 }}
          />
          <Legend iconType="circle" iconSize={8} wrapperStyle={{ fontSize: 12, color: '#6b7280' }} />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
