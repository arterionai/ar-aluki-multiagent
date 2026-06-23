'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { StatCard } from '@/components/ui/StatCard';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { UsageAreaChart } from '@/components/charts/UsageAreaChart';
import { PieBreakdown } from '@/components/charts/PieBreakdown';
import { useBillingData } from '@/hooks/useAdminData';
import { CreditCard, DollarSign, Users, TrendingUp } from 'lucide-react';

export default function BillingPage() {
  const { data, loading, error, refresh } = useBillingData();

  const totalRevenue = data?.revenueByDay.reduce((s, d) => s + d.revenue, 0) ?? 0;
  const totalAccounts = data?.accountsByStatus.reduce((s, d) => s + d.count, 0) ?? 0;
  const activeAccounts = data?.accountsByStatus.find(s => s.status === 'active')?.count ?? 0;

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Billing</h1>
        <p className="text-gray-500 text-sm mt-1">Facturación, suscripciones y créditos</p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              title="Revenue (30d)"
              value={`$${totalRevenue.toFixed(2)}`}
              icon={DollarSign}
              color="green"
            />
            <StatCard
              title="Total Accounts"
              value={totalAccounts.toLocaleString()}
              icon={Users}
              color="blue"
            />
            <StatCard
              title="Active Accounts"
              value={activeAccounts.toLocaleString()}
              icon={TrendingUp}
              color="purple"
            />
            <StatCard
              title="Total Credits"
              value={`$${Number(data.totalCreditBalance).toFixed(2)}`}
              icon={CreditCard}
              color="amber"
            />
          </div>

          <UsageAreaChart
            data={data.revenueByDay.map(d => ({ day: d.day, revenue: d.revenue }))}
            dataKey="revenue"
            color="#10b981"
            label="Revenue por Día (30d)"
          />

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <PieBreakdown
              data={data.accountsByStatus.map(s => ({ name: s.status, value: s.count }))}
              label="Cuentas por Estado"
            />
            <PieBreakdown
              data={data.subscriptionsByState.map(s => ({ name: s.state, value: s.count }))}
              label="Suscripciones por Estado"
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
