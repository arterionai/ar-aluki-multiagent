'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { getCredentials } from '@/lib/auth';
import type {
  OverviewData,
  AiCostsData,
  TenantsData,
  WhatsAppData,
  BillingData,
  SystemData,
} from '@/types/admin';

async function fetchAdmin<T>(path: string, apiKey: string, apiUrl: string): Promise<T> {
  const res = await fetch(`${apiUrl.replace(/\/$/, '')}/api/admin/${path}`, {
    headers: { Authorization: `Bearer ${apiKey}` },
  });
  if (res.status === 401) throw new Error('Unauthorized');
  if (!res.ok) throw new Error(`Admin API error: ${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

export function useAdminData<T>(
  path: string
): { data: T | null; loading: boolean; error: string | null; refresh: () => void } {
  const router = useRouter();
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const creds = getCredentials();
    if (!creds) { router.push('/login'); return; }
    setLoading(true);
    setError(null);
    try {
      const result = await fetchAdmin<T>(path, creds.apiKey, creds.apiUrl);
      setData(result);
    } catch (err) {
      if (err instanceof Error && err.message === 'Unauthorized') {
        router.push('/login');
      } else {
        setError(err instanceof Error ? err.message : 'Unknown error');
      }
    } finally {
      setLoading(false);
    }
  }, [path, router]);

  useEffect(() => { load(); }, [load]);

  return { data, loading, error, refresh: load };
}

export const useOverviewData = () => useAdminData<OverviewData>('overview');
export const useAiCostsData = () => useAdminData<AiCostsData>('ai-costs');
export const useTenantsData = () => useAdminData<TenantsData>('tenants');
export const useWhatsAppData = () => useAdminData<WhatsAppData>('whatsapp');
export const useBillingData = () => useAdminData<BillingData>('billing');
export const useSystemData = () => useAdminData<SystemData>('system');
