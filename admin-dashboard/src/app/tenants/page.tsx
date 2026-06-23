'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { DataTable } from '@/components/ui/DataTable';
import { StatCard } from '@/components/ui/StatCard';
import { useTenantsData } from '@/hooks/useAdminData';
import { Users } from 'lucide-react';
import type { TenantRow } from '@/types/admin';

export default function TenantsPage() {
  const { data, loading, error, refresh } = useTenantsData();

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Tenants</h1>
        <p className="text-gray-500 text-sm mt-1">All registered tenants and their activity</p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <StatCard
              title="Total Tenants"
              value={data.tenants.length}
              icon={Users}
              color="blue"
            />
            <StatCard
              title="Active Tenants"
              value={data.tenants.filter(t => t.lastActivity).length}
              icon={Users}
              color="green"
              subtitle="with recorded activity"
            />
          </div>
          <DataTable<TenantRow>
            columns={[
              { key: 'displayName', header: 'Name', render: (r) => r.displayName ?? <span className="text-gray-400 italic">—</span> },
              { key: 'id', header: 'Tenant ID', render: (r) => <span className="font-mono text-xs text-gray-500">{r.id}</span> },
              { key: 'messageCount', header: 'Messages', render: (r) => r.messageCount.toLocaleString() },
              {
                key: 'lastActivity', header: 'Last Activity',
                render: (r) => r.lastActivity ? new Date(r.lastActivity).toLocaleDateString() : <span className="text-gray-400">Never</span>
              },
              { key: 'createdAt', header: 'Created', render: (r) => new Date(r.createdAt).toLocaleDateString() },
            ]}
            data={data.tenants}
            keyExtractor={(r) => r.id}
            emptyMessage="No tenants found"
          />
        </div>
      )}
    </DashboardLayout>
  );
}
