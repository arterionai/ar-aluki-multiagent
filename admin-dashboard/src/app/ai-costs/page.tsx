'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { UsageAreaChart } from '@/components/charts/UsageAreaChart';
import { CostBarChart } from '@/components/charts/CostBarChart';
import { DataTable } from '@/components/ui/DataTable';
import { useAiCostsData } from '@/hooks/useAdminData';
import type { TenantCost } from '@/types/admin';

export default function AiCostsPage() {
  const { data, loading, error, refresh } = useAiCostsData();

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">AI Costs</h1>
        <p className="text-gray-500 text-sm mt-1">Token usage and cost breakdown (last 30 days)</p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data && (
        <div className="space-y-6">
          <UsageAreaChart data={data.byDay} dataKey="cost" label="Daily Cost (USD)" color="#8b5cf6" />
          <UsageAreaChart data={data.byDay} dataKey="tokens" label="Daily Token Usage" color="#3b82f6" />
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <CostBarChart
              data={data.byFeature}
              dataKey="cost"
              nameKey="feature"
              label="Cost by Feature (USD)"
              color="#10b981"
              formatter={(v) => `$${v.toFixed(4)}`}
            />
            <CostBarChart
              data={data.byFeature}
              dataKey="tokens"
              nameKey="feature"
              label="Tokens by Feature"
              color="#f59e0b"
            />
          </div>
          <div>
            <h3 className="text-gray-700 font-semibold mb-3">Top 10 Tenants by Cost (30d)</h3>
            <DataTable<TenantCost>
              columns={[
                { key: 'tenantId', header: 'Tenant ID' },
                { key: 'tokens', header: 'Tokens', render: (r) => r.tokens.toLocaleString() },
                { key: 'cost', header: 'Cost (USD)', render: (r) => `$${Number(r.cost).toFixed(4)}` },
              ]}
              data={data.topTenants}
              keyExtractor={(r) => r.tenantId}
              emptyMessage="No AI usage recorded yet"
            />
          </div>
        </div>
      )}
    </DashboardLayout>
  );
}
