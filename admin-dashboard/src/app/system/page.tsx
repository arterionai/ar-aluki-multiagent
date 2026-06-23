'use client';

import { DashboardLayout } from '@/components/layout/DashboardLayout';
import { StatCard } from '@/components/ui/StatCard';
import { DataTable } from '@/components/ui/DataTable';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { ErrorBanner } from '@/components/ui/ErrorBanner';
import { useSystemData } from '@/hooks/useAdminData';
import { AlertCircle, Database, Clock, Server, CheckCircle } from 'lucide-react';
import type { TableSize } from '@/types/admin';

export default function SystemPage() {
  const { data, loading, error, refresh } = useSystemData();

  const columns = [
    { key: 'tableName', header: 'Tabla' },
    { key: 'size', header: 'Tamaño' },
    {
      key: 'sizeBytes',
      header: 'Bytes',
      render: (row: TableSize) => row.sizeBytes.toLocaleString(),
    },
  ];

  const healthy = (data?.failedExtractionJobs24h ?? 0) === 0
    && (data?.oldestPendingReminderAgeMinutes ?? 0) < 5;

  return (
    <DashboardLayout>
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">System Health</h1>
        <p className="text-gray-500 text-sm mt-1">Estado del sistema y base de datos</p>
      </div>

      {loading && <LoadingSpinner />}
      {error && <ErrorBanner message={error} onRetry={refresh} />}

      {data && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              title="Estado General"
              value={healthy ? 'Saludable' : 'Atención'}
              icon={healthy ? CheckCircle : AlertCircle}
              color={healthy ? 'green' : 'red'}
            />
            <StatCard
              title="Jobs Fallidos (24h)"
              value={data.failedExtractionJobs24h.toLocaleString()}
              icon={AlertCircle}
              color={data.failedExtractionJobs24h > 0 ? 'red' : 'green'}
            />
            <StatCard
              title="Reminder más Antiguo"
              value={`${data.oldestPendingReminderAgeMinutes} min`}
              icon={Clock}
              color={data.oldestPendingReminderAgeMinutes > 10 ? 'amber' : 'green'}
              subtitle="Tiempo pendiente más antiguo"
            />
            <StatCard
              title="Server UTC"
              value={new Date(data.serverUtc).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' })}
              icon={Server}
              color="blue"
            />
          </div>

          <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-6">
            <div className="flex items-center gap-2 mb-4">
              <Database className="w-5 h-5 text-gray-400" />
              <h2 className="text-gray-700 font-semibold">Tamaño de Tablas (Top 20)</h2>
            </div>
            <DataTable<TableSize>
              columns={columns}
              data={data.tableSizes}
              keyExtractor={row => row.tableName}
              emptyMessage="No se encontraron tablas"
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
