'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { StatCard } from '@/components/ui/StatCard';
import { UsageAreaChart } from '@/components/charts/UsageAreaChart';
import { CostBarChart } from '@/components/charts/CostBarChart';
import { useAiCostsData } from '@/hooks/useAdminData';
import { DollarSign, AlertTriangle } from 'lucide-react';

const fmt = (v: number, currency = 'USD') =>
  new Intl.NumberFormat('en-US', { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v);

export default function AiCostsPage() {
  const { data, loading, error, refresh } = useAiCostsData();

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Platform Costs</h1>
        <p className="text-gray-500 text-sm mt-1">
          Azure resource costs for the <strong>ar-Aluki</strong> resource group — month-to-date &amp; last 30 days
        </p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data?.managedIdentityUnavailable && (
        <div className="flex items-center gap-3 bg-yellow-50 border border-yellow-200 rounded-lg p-4 mb-6">
          <AlertTriangle className="w-5 h-5 text-yellow-500 flex-shrink-0" />
          <p className="text-sm text-yellow-800">
            La managed identity de la Function App no tiene acceso a Cost Management.
            Asigna el rol <strong>Cost Management Reader</strong> al RG <code>ar-Aluki</code>.
          </p>
        </div>
      )}

      {data && !data.managedIdentityUnavailable && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <StatCard
              title="Total mes actual"
              value={fmt(data.totalMtd, data.currency)}
              icon={DollarSign}
              color="green"
            />
            <StatCard
              title="Servicios con consumo"
              value={data.byService.length.toString()}
              icon={DollarSign}
              color="blue"
            />
          </div>

          <UsageAreaChart
            data={data.byDay}
            dataKey="cost"
            label={`Costo diario (${data.currency}) — últimos 30 días`}
            color="#10b981"
          />

          <CostBarChart
            data={data.byService}
            dataKey="cost"
            nameKey="service"
            label={`Costo por servicio (${data.currency}) — mes actual`}
            color="#3b82f6"
            formatter={(v) => fmt(v, data.currency)}
          />
        </div>
      )}
    </DashboardLayout>
  );
}
