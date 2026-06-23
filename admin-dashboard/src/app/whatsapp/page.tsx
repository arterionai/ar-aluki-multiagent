'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { UsageAreaChart } from '@/components/charts/UsageAreaChart';
import { PieBreakdown } from '@/components/charts/PieBreakdown';
import { useWhatsAppData } from '@/hooks/useAdminData';

export default function WhatsAppPage() {
  const { data, loading, error, refresh } = useWhatsAppData();

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">WhatsApp</h1>
        <p className="text-gray-500 text-sm mt-1">Message volume and delivery statistics (last 30 days)</p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data && (
        <div className="space-y-6">
          <UsageAreaChart data={data.inboundByDay} dataKey="count" label="Inbound Messages per Day" color="#3b82f6" />
          <UsageAreaChart data={data.outboundByDay} dataKey="count" label="Outbound Messages per Day" color="#10b981" />
          {data.agentDistribution.length > 0 && (
            <PieBreakdown
              data={data.agentDistribution.map(a => ({ name: a.agent, value: a.count }))}
              label="Agent Distribution (30d)"
            />
          )}
        </div>
      )}
    </DashboardLayout>
  );
}
