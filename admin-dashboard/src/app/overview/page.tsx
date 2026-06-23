'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { StatCard } from '@/components/ui/StatCard';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { useOverviewData } from '@/hooks/useAdminData';
import { MessageSquare, Users, Send, TrendingUp, UserPlus } from 'lucide-react';

export default function OverviewPage() {
  const { data, loading, error, refresh } = useOverviewData();

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Overview</h1>
        <p className="text-gray-500 text-sm mt-1">Platform activity at a glance</p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              title="Messages (24h)"
              value={data.messages24h.toLocaleString()}
              icon={MessageSquare}
              color="blue"
            />
            <StatCard
              title="Messages (7d)"
              value={data.messages7d.toLocaleString()}
              icon={TrendingUp}
              color="purple"
            />
            <StatCard
              title="Messages (30d)"
              value={data.messages30d.toLocaleString()}
              icon={TrendingUp}
              color="amber"
            />
            <StatCard
              title="Active Tenants (30d)"
              value={data.activeTenants.toLocaleString()}
              icon={Users}
              color="green"
            />
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            <StatCard
              title="Outbound Messages (30d)"
              value={data.outboundMessages.toLocaleString()}
              icon={Send}
              color="blue"
            />
            <StatCard
              title="Total Usuarios"
              value={data.totalUsers.toLocaleString()}
              icon={Users}
              color="green"
            />
            <StatCard
              title="Usuarios Nuevos Hoy"
              value={data.newUsersToday.toLocaleString()}
              icon={UserPlus}
              color="purple"
            />
          </div>
          <div className="text-xs text-gray-400 text-right">
            Last updated: {new Date(data.generatedAt).toLocaleString()}
          </div>
        </div>
      )}
    </DashboardLayout>
  );
}
